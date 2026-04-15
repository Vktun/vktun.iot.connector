using System.Collections.Concurrent;
using System.Text.Json;
using Vktun.IoT.Connector.Business.Factories;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;
using Vktun.IoT.Connector.Protocol.Factories;
using Vktun.IoT.Connector.Protocol.Parsers;

namespace Vktun.IoT.Connector.Business.Services;

public class DeviceCommandExecutor : IDeviceCommandExecutor, IAsyncDisposable
{
    private readonly ICommunicationChannelFactory _channelFactory;
    private readonly IProtocolParserFactory _parserFactory;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly ILogger _logger;
    private readonly IResourceMonitor? _resourceMonitor;
    private readonly ConcurrentDictionary<string, DeviceRuntimeContext> _contexts = new();

    public event EventHandler<DataReceivedEventArgs>? DataReceived;

    public DeviceCommandExecutor(
        ICommunicationChannelFactory channelFactory,
        IProtocolParserFactory parserFactory,
        IConfigurationProvider configurationProvider,
        ILogger logger,
        IResourceMonitor? resourceMonitor = null)
    {
        _channelFactory = channelFactory;
        _parserFactory = parserFactory;
        _configurationProvider = configurationProvider;
        _logger = logger;
        _resourceMonitor = resourceMonitor;
    }

    public async Task<bool> ConnectAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        if (_contexts.ContainsKey(device.DeviceId))
        {
            return true;
        }

        var validation = ConnectionSettingsValidator.ValidateAndNormalize(device);
        if (!validation.IsValid || validation.Settings == null)
        {
            _logger.Warning($"Device {device.DeviceId} has invalid connection settings: {validation.ErrorMessage}");
            return false;
        }

        ConnectionSettingsValidator.ApplyNormalizedSettings(device, validation.Settings);

        var protocolConfig = await GetProtocolConfigAsync(device, cancellationToken).ConfigureAwait(false);
        if (protocolConfig == null)
        {
            _logger.Warning($"No protocol configuration was found for device {device.DeviceId}.");
            return false;
        }

        var parser = _parserFactory.GetParser(protocolConfig.ProtocolId) ?? _parserFactory.GetParser(protocolConfig.ProtocolType);
        if (parser == null)
        {
            _logger.Warning($"No parser is registered for protocol {protocolConfig.ProtocolId}/{protocolConfig.ProtocolType}.");
            return false;
        }

        var channel = _channelFactory.CreateChannel(device);
        channel.DataReceived += OnChannelDataReceived;
        channel.ErrorOccurred += OnChannelError;

        if (!await channel.OpenAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (!await channel.ConnectDeviceAsync(device, cancellationToken).ConfigureAwait(false))
        {
            await channel.CloseAsync().ConfigureAwait(false);
            channel.DataReceived -= OnChannelDataReceived;
            channel.ErrorOccurred -= OnChannelError;
            return false;
        }

        protocolConfig.ChannelId = channel.ChannelId;
        var context = new DeviceRuntimeContext(device, protocolConfig, parser, channel);
        _contexts[device.DeviceId] = context;
        _resourceMonitor?.TrackChannel(channel);
        _resourceMonitor?.RegisterDevice(device, channel.ChannelId, protocolConfig.ProtocolId, protocolConfig.ProtocolType);
        _logger.Info($"Device connected. {FormatLogContext(context, taskId: string.Empty)}");
        return true;
    }

    public async Task DisconnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (_contexts.TryRemove(deviceId, out var context))
        {
            var channelId = context.Channel.ChannelId;
            context.Channel.DataReceived -= OnChannelDataReceived;
            context.Channel.ErrorOccurred -= OnChannelError;
            await context.Channel.DisconnectDeviceAsync(deviceId).ConfigureAwait(false);
            await context.Channel.CloseAsync().ConfigureAwait(false);
            _resourceMonitor?.UnregisterDevice(deviceId);
            _resourceMonitor?.UntrackChannel(channelId);
            context.Dispose();
            _logger.Info($"Device disconnected. {FormatLogContext(context, taskId: string.Empty)}");
        }
    }

    public async Task<CommandResult> ExecuteAsync(DeviceCommand command, DeviceInfo device, CancellationToken cancellationToken = default)
    {
        if (!_contexts.TryGetValue(device.DeviceId, out var context))
        {
            return new CommandResult
            {
                CommandId = command.CommandId,
                Success = false,
                ErrorMessage = $"Device {device.DeviceId} is not connected."
            };
        }

        await context.ExecutionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var startedAt = DateTime.Now;

        try
        {
            if (string.Equals(command.CommandName, "CollectData", StringComparison.OrdinalIgnoreCase))
            {
                return await ExecuteCollectAsync(context, command, cancellationToken).ConfigureAwait(false);
            }

            var requestData = BuildRequestPayload(context, command);
            if (requestData.Length == 0)
            {
                return new CommandResult
                {
                    CommandId = command.CommandId,
                    Success = false,
                    ErrorMessage = $"No request payload could be produced for command {command.CommandName}.",
                    ElapsedTime = DateTime.Now - startedAt
                };
            }

            var response = await SendAndReceiveAsync(context, requestData, command.Timeout, command.CommandId, cancellationToken).ConfigureAwait(false);
            var parsed = TryParseResponse(context, response, requestData, command.CommandId);

            return new CommandResult
            {
                CommandId = command.CommandId,
                Success = true,
                RequestData = requestData,
                ResponseData = response,
                ParsedData = parsed,
                ElapsedTime = DateTime.Now - startedAt
            };
        }
        catch (OperationCanceledException)
        {
            return new CommandResult
            {
                CommandId = command.CommandId,
                Success = false,
                ErrorMessage = $"Command {command.CommandName} timed out.",
                ElapsedTime = DateTime.Now - startedAt
            };
        }
        catch (Exception ex)
        {
            return new CommandResult
            {
                CommandId = command.CommandId,
                Success = false,
                ErrorMessage = ex.Message,
                ElapsedTime = DateTime.Now - startedAt
            };
        }
        finally
        {
            context.ExecutionLock.Release();
        }
    }

    public async Task<ProtocolConfig?> GetProtocolConfigAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        if (_contexts.TryGetValue(device.DeviceId, out var existing))
        {
            return existing.ProtocolConfig;
        }

        if (device.ExtendedProperties.TryGetValue("ProtocolConfig", out var configValue) && configValue is ProtocolConfig protocolConfig)
        {
            return protocolConfig;
        }

        if (!string.IsNullOrWhiteSpace(device.ProtocolConfigPath) && File.Exists(device.ProtocolConfigPath))
        {
            return await _configurationProvider.LoadProtocolTemplateAsync(device.ProtocolConfigPath).ConfigureAwait(false);
        }

        foreach (var searchDirectory in GetSearchDirectories())
        {
            if (!Directory.Exists(searchDirectory))
            {
                continue;
            }

            var templates = await _configurationProvider.LoadProtocolTemplatesAsync(searchDirectory).ConfigureAwait(false);
            var match = templates.FirstOrDefault(candidate =>
                (!string.IsNullOrWhiteSpace(device.ProtocolId) &&
                 candidate.ProtocolId.Equals(device.ProtocolId, StringComparison.OrdinalIgnoreCase)) ||
                candidate.ProtocolType == device.ProtocolType);

            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var deviceId in _contexts.Keys.ToArray())
        {
            await DisconnectAsync(deviceId).ConfigureAwait(false);
        }
    }

    private async Task<CommandResult> ExecuteCollectAsync(DeviceRuntimeContext context, DeviceCommand command, CancellationToken cancellationToken)
    {
        return context.ProtocolConfig.ProtocolType switch
        {
            ProtocolType.ModbusRtu or ProtocolType.ModbusTcp => await ExecuteModbusCollectAsync(context, command, cancellationToken).ConfigureAwait(false),
            ProtocolType.S7 => await ExecuteS7CollectAsync(context, command, cancellationToken).ConfigureAwait(false),
            ProtocolType.IEC104 => await ExecuteIec104CollectAsync(context, command, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"CollectData is not implemented for protocol {context.ProtocolConfig.ProtocolType}.")
        };
    }

    private async Task<CommandResult> ExecuteModbusCollectAsync(DeviceRuntimeContext context, DeviceCommand command, CancellationToken cancellationToken)
    {
        var modbusConfig = context.ProtocolConfig.GetDefinition<ModbusConfig>()
            ?? JsonSerializer.Deserialize<ModbusConfig>(context.ProtocolConfig.ParseRules["ModbusConfig"]);
        if (modbusConfig == null || modbusConfig.Points.Count == 0)
        {
            throw new InvalidOperationException($"Modbus configuration for device {context.Device.DeviceId} does not contain any points.");
        }

        var allPoints = new List<DataPoint>();
        var allResponses = new List<byte>();
        var groups = modbusConfig.Points.GroupBy(point => point.RegisterType);

        foreach (var group in groups)
        {
            var startAddress = group.Min(point => point.Address);
            var endAddress = group.Max(point => point.Address + Math.Max(GetPointRegisterSpan(point), point.Quantity) - 1);
            var quantity = (ushort)(endAddress - startAddress + 1);

            var request = new DeviceCommand
            {
                DeviceId = context.Device.DeviceId,
                CommandId = command.CommandId,
                CommandName = GetReadCommandName(group.Key),
                Timeout = command.Timeout
            };
            request.Parameters["Address"] = startAddress;
            request.Parameters["Quantity"] = quantity;

            var requestPayload = context.Parser.Pack(request, context.ProtocolConfig);
            var response = await SendAndReceiveAsync(context, requestPayload, command.Timeout, command.CommandId, cancellationToken).ConfigureAwait(false);
            allResponses.AddRange(response);

            var adjustedConfig = CloneProtocolConfig(context.ProtocolConfig);
            adjustedConfig.SetDefinition(new ModbusConfig
            {
                ProtocolId = modbusConfig.ProtocolId,
                ProtocolName = modbusConfig.ProtocolName,
                Description = modbusConfig.Description,
                ModbusType = modbusConfig.ModbusType,
                SlaveId = modbusConfig.SlaveId,
                ByteOrder = modbusConfig.ByteOrder,
                WordOrder = modbusConfig.WordOrder,
                ResponseTimeout = modbusConfig.ResponseTimeout,
                InterFrameDelay = modbusConfig.InterFrameDelay,
                Points = group.Select(point => new ModbusPointConfig
                {
                    PointName = point.PointName,
                    RegisterType = point.RegisterType,
                    Address = (ushort)(point.Address - startAddress),
                    Quantity = point.Quantity,
                    DataType = point.DataType,
                    Ratio = point.Ratio,
                    OffsetValue = point.OffsetValue,
                    Unit = point.Unit,
                    MinValue = point.MinValue,
                    MaxValue = point.MaxValue,
                    IsReadOnly = point.IsReadOnly,
                    Description = point.Description,
                    ScanRate = point.ScanRate
                }).ToList()
            });
            adjustedConfig.ParseRules["ModbusConfig"] = adjustedConfig.DefinitionJson;

            var parsed = context.Parser.Parse(response, adjustedConfig).FirstOrDefault();
            if (parsed != null)
            {
                allPoints.AddRange(parsed.DataItems);
            }
            else
            {
                RecordParseFailure(context, requestPayload, response, command.CommandId, adjustedConfig.ConfigVersion, "Modbus parser returned no data.");
            }
        }

        var data = new DeviceData
        {
            DeviceId = context.Device.DeviceId,
            ChannelId = context.Channel.ChannelId,
            ProtocolType = context.ProtocolConfig.ProtocolType,
            CollectTime = DateTime.Now,
            DataItems = allPoints,
            RawData = allResponses.ToArray(),
            IsValid = true
        };

        return new CommandResult
        {
            CommandId = command.CommandId,
            Success = true,
            ResponseData = allResponses.ToArray(),
            ParsedData = data,
            ElapsedTime = TimeSpan.Zero
        };
    }

    private async Task<CommandResult> ExecuteS7CollectAsync(DeviceRuntimeContext context, DeviceCommand command, CancellationToken cancellationToken)
    {
        if (context.Parser is not S7ProtocolParser s7Parser)
        {
            throw new InvalidOperationException("S7 parser is not available.");
        }

        var s7Config = context.ProtocolConfig.GetDefinition<S7Config>();
        if (s7Config == null || s7Config.Points.Count == 0)
        {
            throw new InvalidOperationException("S7 configuration does not contain any points.");
        }

        var request = new S7ReadRequest
        {
            Items = s7Config.Points.Select(point => new S7DataItem
            {
                Area = Enum.Parse<S7Area>(point.Area, true),
                DbNumber = point.DbNumber,
                StartAddress = point.StartAddress,
                BitPosition = point.BitPosition,
                Type = MapS7DataType(point.DataType),
                Length = Math.Max(1, GetByteCount(point.DataType))
            }).ToList()
        };

        var payload = s7Parser.BuildReadCommand(request, s7Config);
        var response = await SendAndReceiveAsync(context, payload, command.Timeout, command.CommandId, cancellationToken).ConfigureAwait(false);
        var parsed = TryParseResponse(context, response, payload, command.CommandId);

        return new CommandResult
        {
            CommandId = command.CommandId,
            Success = parsed != null,
            RequestData = payload,
            ResponseData = response,
            ParsedData = parsed,
            ElapsedTime = TimeSpan.Zero
        };
    }

    private async Task<CommandResult> ExecuteIec104CollectAsync(DeviceRuntimeContext context, DeviceCommand command, CancellationToken cancellationToken)
    {
        if (context.Parser is not IEC104ProtocolParser iec104Parser)
        {
            throw new InvalidOperationException("IEC104 parser is not available.");
        }

        var config = context.ProtocolConfig.GetDefinition<IEC104Config>();
        if (config == null)
        {
            throw new InvalidOperationException("IEC104 configuration is missing.");
        }

        var payload = iec104Parser.BuildInterrogationCommand(config.CommonAddress, (byte)IEC104QualifierOfInterrogation.Station_Interrogation);
        var response = await SendAndReceiveAsync(context, payload, command.Timeout, command.CommandId, cancellationToken).ConfigureAwait(false);
        var parsed = TryParseResponse(context, response, payload, command.CommandId);

        return new CommandResult
        {
            CommandId = command.CommandId,
            Success = parsed != null,
            RequestData = payload,
            ResponseData = response,
            ParsedData = parsed,
            ElapsedTime = TimeSpan.Zero
        };
    }

    private async Task<byte[]> SendAndReceiveAsync(
        DeviceRuntimeContext context,
        byte[] requestData,
        int timeoutMs,
        string taskId,
        CancellationToken cancellationToken)
    {
        var timeout = timeoutMs > 0 ? timeoutMs : context.Device.ConnectionMode == ConnectionMode.Client ? context.GetDefaultTimeout() : 5000;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var responseSource = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.PendingResponse = responseSource;

        try
        {
            _logger.Debug($"Request frame sent. {FormatLogContext(context, taskId)} bytes={requestData.Length} frame={FormatFrame(requestData)}");

            var sentBytes = await context.Channel.SendAsync(context.Device.DeviceId, requestData, timeoutCts.Token).ConfigureAwait(false);
            if (sentBytes <= 0)
            {
                throw new InvalidOperationException($"No bytes were sent to device {context.Device.DeviceId}.");
            }

            var response = await responseSource.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            stopwatch.Stop();
            RecordOperation(context, taskId, requestData, response, stopwatch.Elapsed, success: true);
            _logger.Debug($"Response frame received. {FormatLogContext(context, taskId)} bytes={response.Length} elapsedMs={stopwatch.ElapsedMilliseconds} frame={FormatFrame(response)}");
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            RecordOperation(
                context,
                taskId,
                requestData,
                Array.Empty<byte>(),
                stopwatch.Elapsed,
                success: false,
                timedOut: true,
                errorMessage: $"Request timed out after {timeout}ms.");
            _logger.Warning($"Request timed out. {FormatLogContext(context, taskId)} timeoutMs={timeout} elapsedMs={stopwatch.ElapsedMilliseconds}");
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            RecordOperation(
                context,
                taskId,
                requestData,
                Array.Empty<byte>(),
                stopwatch.Elapsed,
                success: false,
                exceptionOccurred: true,
                errorMessage: ex.Message);
            _logger.Error($"Request failed. {FormatLogContext(context, taskId)} elapsedMs={stopwatch.ElapsedMilliseconds} error={ex.Message}", ex);
            throw;
        }
        finally
        {
            context.PendingResponse = null;
        }
    }

    private byte[] BuildRequestPayload(DeviceRuntimeContext context, DeviceCommand command)
    {
        if (command.Data is { Length: > 0 })
        {
            return command.Data;
        }

        return context.Parser.Pack(command, context.ProtocolConfig);
    }

    private DeviceData? TryParseResponse(DeviceRuntimeContext context, byte[] responseData, byte[] requestData, string taskId)
    {
        try
        {
            if (!context.Parser.Validate(responseData, context.ProtocolConfig))
            {
                RecordParseFailure(context, requestData, responseData, taskId, context.ProtocolConfig.ConfigVersion, "Protocol validation failed.");
                return null;
            }

            var parsed = context.Parser.Parse(responseData, context.ProtocolConfig).FirstOrDefault();
            if (parsed == null)
            {
                RecordParseFailure(context, requestData, responseData, taskId, context.ProtocolConfig.ConfigVersion, "Parser returned no data.");
                return null;
            }

            parsed.DeviceId = context.Device.DeviceId;
            parsed.ChannelId = context.Channel.ChannelId;
            context.Device.LastDataTime = parsed.CollectTime;
            return parsed;
        }
        catch (Exception ex)
        {
            RecordParseFailure(context, requestData, responseData, taskId, context.ProtocolConfig.ConfigVersion, ex.Message);
            _logger.Error($"Response parse failed. {FormatLogContext(context, taskId)} error={ex.Message}", ex);
            return null;
        }
    }

    private void OnChannelDataReceived(object? sender, DataReceivedEventArgs e)
    {
        if (_contexts.TryGetValue(e.DeviceId, out var context))
        {
            context.PendingResponse?.TrySetResult(e.Data);
        }

        DataReceived?.Invoke(this, e);
    }

    private void OnChannelError(object? sender, ChannelErrorEventArgs e)
    {
        if (_contexts.TryGetValue(e.DeviceId, out var context))
        {
            context.PendingResponse?.TrySetException(e.Exception ?? new InvalidOperationException(e.Message));
            _logger.Error($"Channel error. {FormatLogContext(context, taskId: string.Empty)} error={e.Message}", e.Exception);
        }
    }

    private void RecordOperation(
        DeviceRuntimeContext context,
        string taskId,
        byte[] requestData,
        byte[] responseData,
        TimeSpan elapsedTime,
        bool success,
        bool timedOut = false,
        bool exceptionOccurred = false,
        string? errorMessage = null)
    {
        _resourceMonitor?.RecordOperation(new ResourceOperationRecord
        {
            Timestamp = DateTime.Now,
            DeviceId = context.Device.DeviceId,
            ChannelId = context.Channel.ChannelId,
            ProtocolId = context.ProtocolConfig.ProtocolId,
            ProtocolType = context.ProtocolConfig.ProtocolType,
            TaskId = taskId,
            ElapsedTime = elapsedTime,
            Success = success,
            TimedOut = timedOut,
            ExceptionOccurred = exceptionOccurred,
            ErrorMessage = errorMessage,
            ConfigVersion = context.ProtocolConfig.ConfigVersion,
            RequestBytes = requestData.Length,
            ResponseBytes = responseData.Length,
            RequestFrame = requestData,
            ResponseFrame = responseData
        });
    }

    private void RecordParseFailure(
        DeviceRuntimeContext context,
        byte[] requestData,
        byte[] responseData,
        string taskId,
        int configVersion,
        string parseError)
    {
        _resourceMonitor?.RecordDiagnosticTrace(new DiagnosticTrace
        {
            Timestamp = DateTime.Now,
            DeviceId = context.Device.DeviceId,
            ChannelId = context.Channel.ChannelId,
            ProtocolId = context.ProtocolConfig.ProtocolId,
            ProtocolType = context.ProtocolConfig.ProtocolType,
            TaskId = taskId,
            ConfigVersion = configVersion,
            Success = false,
            RequestBytes = requestData.Length,
            ResponseBytes = responseData.Length,
            RequestFrameHex = Convert.ToHexString(requestData),
            ResponseFrameHex = Convert.ToHexString(responseData),
            ParseError = parseError
        });

        _logger.Warning($"Response parse failed. {FormatLogContext(context, taskId)} parseError={parseError} requestFrame={FormatFrame(requestData)} responseFrame={FormatFrame(responseData)}");
    }

    private static string FormatLogContext(DeviceRuntimeContext context, string taskId)
    {
        return $"deviceId={context.Device.DeviceId} channelId={context.Channel.ChannelId} protocolId={context.ProtocolConfig.ProtocolId} protocolType={context.ProtocolConfig.ProtocolType} taskId={taskId} configVersion={context.ProtocolConfig.ConfigVersion}";
    }

    private static string FormatFrame(byte[] frame, int maxBytes = 128)
    {
        if (frame.Length == 0)
        {
            return string.Empty;
        }

        var visibleBytes = frame.Length <= maxBytes ? frame : frame[..maxBytes];
        var hex = Convert.ToHexString(visibleBytes);
        return frame.Length <= maxBytes ? hex : $"{hex}...(+{frame.Length - maxBytes} bytes)";
    }

    private static ProtocolConfig CloneProtocolConfig(ProtocolConfig source)
    {
        return new ProtocolConfig
        {
            ProtocolId = source.ProtocolId,
            ProtocolName = source.ProtocolName,
            Description = source.Description,
            ProtocolType = source.ProtocolType,
            ProtocolVersion = source.ProtocolVersion,
            ChannelId = source.ChannelId,
            Vendor = source.Vendor,
            DeviceModel = source.DeviceModel,
            TemplateSource = source.TemplateSource,
            DefinitionJson = source.DefinitionJson,
            ParseRules = new Dictionary<string, string>(source.ParseRules),
            Points = source.Points
        };
    }

    private static string GetReadCommandName(ModbusRegisterType registerType)
    {
        return registerType switch
        {
            ModbusRegisterType.Coil => "ReadCoils",
            ModbusRegisterType.DiscreteInput => "ReadDiscreteInputs",
            ModbusRegisterType.HoldingRegister => "ReadHoldingRegisters",
            _ => "ReadInputRegisters"
        };
    }

    private static int GetPointRegisterSpan(ModbusPointConfig point)
    {
        return point.RegisterType is ModbusRegisterType.Coil or ModbusRegisterType.DiscreteInput
            ? Math.Max(1, (int)point.Quantity)
            : Math.Max(1, GetByteCount(point.DataType) / 2);
    }

    private static int GetByteCount(DataType dataType)
    {
        return dataType switch
        {
            DataType.UInt8 or DataType.Int8 => 1,
            DataType.UInt16 or DataType.Int16 => 2,
            DataType.UInt32 or DataType.Int32 or DataType.Float => 4,
            DataType.UInt64 or DataType.Int64 or DataType.Double => 8,
            _ => 2
        };
    }

    private static S7DataItemType MapS7DataType(DataType dataType)
    {
        return dataType switch
        {
            DataType.UInt8 or DataType.Int8 => S7DataItemType.Byte,
            DataType.UInt16 or DataType.Int16 => S7DataItemType.Word,
            DataType.UInt32 or DataType.Int32 => S7DataItemType.DWord,
            DataType.Float => S7DataItemType.Real,
            DataType.Bit => S7DataItemType.Bit,
            _ => S7DataItemType.Word
        };
    }

    private static IEnumerable<string> GetSearchDirectories()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var currentDirectory = Directory.GetCurrentDirectory();

        return new[]
        {
            Path.Combine(baseDirectory, "Protocols"),
            Path.Combine(currentDirectory, "Protocols"),
            Path.Combine(currentDirectory, "demo", "Vktun.IoT.Connector.Demo", "Protocols"),
            Path.Combine(currentDirectory, "src", "Vktun.IoT.Connector.Protocol", "Templates")
        }.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class DeviceRuntimeContext : IDisposable
    {
        public DeviceRuntimeContext(DeviceInfo device, ProtocolConfig protocolConfig, IProtocolParser parser, ICommunicationChannel channel)
        {
            Device = device;
            ProtocolConfig = protocolConfig;
            Parser = parser;
            Channel = channel;
        }

        public DeviceInfo Device { get; }
        public ProtocolConfig ProtocolConfig { get; }
        public IProtocolParser Parser { get; }
        public ICommunicationChannel Channel { get; }
        public SemaphoreSlim ExecutionLock { get; } = new(1, 1);
        public TaskCompletionSource<byte[]>? PendingResponse { get; set; }

        public int GetDefaultTimeout()
        {
            return ProtocolConfig.ProtocolType switch
            {
                ProtocolType.ModbusRtu or ProtocolType.ModbusTcp => ProtocolConfig.GetDefinition<ModbusConfig>()?.ResponseTimeout ?? 1000,
                ProtocolType.S7 => ProtocolConfig.GetDefinition<S7Config>()?.ResponseTimeout ?? 3000,
                ProtocolType.IEC104 => ProtocolConfig.GetDefinition<IEC104Config>()?.ResponseTimeout ?? 5000,
                _ => 5000
            };
        }

        public void Dispose()
        {
            ExecutionLock.Dispose();
        }
    }
}
