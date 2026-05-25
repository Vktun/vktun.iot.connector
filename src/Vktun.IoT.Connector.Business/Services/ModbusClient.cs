using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;

namespace Vktun.IoT.Connector.Business.Services;

public class ModbusClient : IModbusClient
{
    private readonly IDeviceCommandExecutor _commandExecutor;
    private readonly IProtocolParserFactory _parserFactory;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, ConnectionState> _connections = new();

    public ModbusClient(IDeviceCommandExecutor commandExecutor, IProtocolParserFactory parserFactory, ILogger logger)
    {
        _commandExecutor = commandExecutor;
        _parserFactory = parserFactory;
        _logger = logger;
    }

    public async Task<ModbusOperationResult> ConnectAsync(ModbusConnectionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.ProtocolType is not (ProtocolType.ModbusTcp or ProtocolType.ModbusRtu))
        {
            return Failure(options.ConnectionId, $"Unsupported Modbus protocol type: {options.ProtocolType}");
        }

        var device = CreateDevice(options);
        var protocolConfig = CreateProtocolConfig(options);
        device.ExtendedProperties["ProtocolConfig"] = protocolConfig;

        var connected = await _commandExecutor.ConnectAsync(device, cancellationToken).ConfigureAwait(false);
        if (!connected)
        {
            return Failure(options.ConnectionId, "Failed to connect Modbus device.");
        }

        _connections[options.ConnectionId] = new ConnectionState(options, device, protocolConfig);
        return new ModbusOperationResult
        {
            ConnectionId = options.ConnectionId,
            Success = true,
            Timestamp = DateTime.Now
        };
    }

    public async Task<ModbusOperationResult> DisconnectAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        if (!_connections.TryRemove(connectionId, out var state))
        {
            return Failure(connectionId, $"Modbus connection '{connectionId}' is not connected.");
        }

        await _commandExecutor.DisconnectAsync(state.Device.DeviceId, cancellationToken).ConfigureAwait(false);
        return new ModbusOperationResult
        {
            ConnectionId = connectionId,
            Success = true,
            Timestamp = DateTime.Now
        };
    }

    public async Task<ModbusReadResult> ReadAsync(ModbusReadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_connections.TryGetValue(request.ConnectionId, out var state))
        {
            return ReadFailure(request, $"Modbus connection '{request.ConnectionId}' is not connected.");
        }

        if (request.Quantity < 1)
        {
            return ReadFailure(request, "Modbus read quantity must be greater than zero.");
        }

        var command = new DeviceCommand
        {
            DeviceId = state.Device.DeviceId,
            CommandName = GetReadCommandName(request.RegisterType),
            Timeout = request.Timeout
        };
        command.Parameters["Address"] = request.Address;
        command.Parameters["Quantity"] = request.Quantity;

        var result = await _commandExecutor.ExecuteAsync(command, state.Device, cancellationToken).ConfigureAwait(false);
        var readResult = CreateReadResult(request, result);
        if (!result.Success)
        {
            readResult.ErrorMessage = result.ErrorMessage ?? "Modbus read failed.";
            return readResult;
        }

        if (result.ResponseData is not { Length: > 0 } response)
        {
            readResult.ErrorMessage = "Modbus read returned no response frame.";
            return readResult;
        }

        var protocolConfig = CreateProtocolConfig(state.Options, request);
        var parser = _parserFactory.GetParser(protocolConfig.ProtocolType);
        if (parser == null || !parser.Validate(response, protocolConfig))
        {
            readResult.ErrorMessage = "Modbus read response frame is invalid.";
            return readResult;
        }

        var responseInfo = TryReadResponsePayload(state.Options.ProtocolType, response);
        if (!responseInfo.Success)
        {
            readResult.ErrorMessage = responseInfo.ErrorMessage;
            return readResult;
        }

        if (responseInfo.ExceptionCode != null)
        {
            readResult.ErrorMessage = $"Modbus exception response: {responseInfo.ExceptionCode:X2}.";
            return readResult;
        }

        PopulateReadValues(readResult, request, responseInfo.Payload);
        readResult.Success = true;
        return readResult;
    }

    public async Task<ModbusOperationResult> WriteAsync(ModbusWriteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_connections.TryGetValue(request.ConnectionId, out var state))
        {
            return Failure(request.ConnectionId, $"Modbus connection '{request.ConnectionId}' is not connected.");
        }

        if (request.RegisterType is ModbusRegisterType.DiscreteInput or ModbusRegisterType.InputRegister)
        {
            return Failure(request.ConnectionId, $"Register type {request.RegisterType} is read-only.");
        }

        var command = CreateWriteCommand(request, state.Device.DeviceId);
        var result = await _commandExecutor.ExecuteAsync(command, state.Device, cancellationToken).ConfigureAwait(false);
        var operation = new ModbusOperationResult
        {
            ConnectionId = request.ConnectionId,
            Success = result.Success,
            ErrorMessage = result.ErrorMessage,
            RequestFrame = result.RequestData,
            ResponseFrame = result.ResponseData,
            ElapsedTime = result.ElapsedTime,
            Timestamp = DateTime.Now
        };

        if (!result.Success || result.ResponseData is not { Length: > 0 } response)
        {
            operation.Success = false;
            operation.ErrorMessage ??= "Modbus write failed.";
            return operation;
        }

        var parser = _parserFactory.GetParser(state.ProtocolConfig.ProtocolType);
        if (parser == null || !parser.Validate(response, state.ProtocolConfig))
        {
            operation.Success = false;
            operation.ErrorMessage = "Modbus write response frame is invalid.";
            return operation;
        }

        var responseInfo = TryReadResponsePayload(state.Options.ProtocolType, response);
        if (!responseInfo.Success || responseInfo.ExceptionCode != null)
        {
            operation.Success = false;
            operation.ErrorMessage = responseInfo.ExceptionCode == null
                ? responseInfo.ErrorMessage
                : $"Modbus exception response: {responseInfo.ExceptionCode:X2}.";
        }

        return operation;
    }

    public async IAsyncEnumerable<ModbusReadResult> PollAsync(
        ModbusPollRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var count = 0;
        while (!cancellationToken.IsCancellationRequested &&
               (request.MaxReadCount == null || count < request.MaxReadCount.Value))
        {
            yield return await ReadAsync(request.ReadRequest, cancellationToken).ConfigureAwait(false);
            count++;

            if (request.MaxReadCount != null && count >= request.MaxReadCount.Value)
            {
                yield break;
            }

            try
            {
                await Task.Delay(Math.Max(1, request.IntervalMs), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
        }
    }

    private static DeviceInfo CreateDevice(ModbusConnectionOptions options)
    {
        return new DeviceInfo
        {
            DeviceId = options.ConnectionId,
            DeviceName = options.ConnectionId,
            CommunicationType = options.ProtocolType == ProtocolType.ModbusTcp ? CommunicationType.Tcp : CommunicationType.Serial,
            ConnectionMode = ConnectionMode.Client,
            IpAddress = options.IpAddress,
            Port = options.Port,
            SerialPort = options.PortName,
            BaudRate = options.BaudRate,
            DataBits = options.DataBits,
            Parity = options.Parity,
            StopBits = options.StopBits,
            SlaveId = options.SlaveId,
            ProtocolType = options.ProtocolType,
            ProtocolId = $"{options.ProtocolType}_{options.ConnectionId}"
        };
    }

    private static ProtocolConfig CreateProtocolConfig(ModbusConnectionOptions options, ModbusReadRequest? request = null)
    {
        var config = new ProtocolConfig
        {
            ProtocolId = $"{options.ProtocolType}_{options.ConnectionId}",
            ProtocolName = options.ProtocolType.ToString(),
            ProtocolType = options.ProtocolType
        };

        var definition = new ModbusConfig
        {
            ProtocolId = config.ProtocolId,
            ProtocolName = config.ProtocolName,
            ModbusType = options.ProtocolType == ProtocolType.ModbusTcp ? ModbusType.Tcp : ModbusType.Rtu,
            SlaveId = options.SlaveId,
            ByteOrder = request?.ByteOrder ?? options.ByteOrder,
            WordOrder = request?.WordOrder ?? options.WordOrder,
            ResponseTimeout = request?.Timeout ?? options.Timeout
        };

        if (request != null)
        {
            definition.Points.Add(new ModbusPointConfig
            {
                PointName = "Value",
                RegisterType = request.RegisterType,
                Address = 0,
                Quantity = request.Quantity,
                DataType = request.DataType
            });
        }

        config.SetDefinition(definition);
        config.ParseRules["ModbusConfig"] = config.DefinitionJson;
        return config;
    }

    private static string GetReadCommandName(ModbusRegisterType registerType)
    {
        return registerType switch
        {
            ModbusRegisterType.Coil => "ReadCoils",
            ModbusRegisterType.DiscreteInput => "ReadDiscreteInputs",
            ModbusRegisterType.HoldingRegister => "ReadHoldingRegisters",
            ModbusRegisterType.InputRegister => "ReadInputRegisters",
            _ => throw new NotSupportedException($"Unsupported Modbus register type: {registerType}")
        };
    }

    private static DeviceCommand CreateWriteCommand(ModbusWriteRequest request, string deviceId)
    {
        var command = new DeviceCommand
        {
            DeviceId = deviceId,
            Timeout = request.Timeout
        };
        command.Parameters["Address"] = request.Address;

        if (request.RegisterType == ModbusRegisterType.Coil)
        {
            var values = request.Values.Count > 0
                ? request.Values.Select(Convert.ToBoolean).ToArray()
                : new[] { Convert.ToBoolean(request.Value) };

            command.CommandName = values.Length == 1 ? "WriteSingleCoil" : "WriteMultipleCoils";
            if (values.Length == 1)
            {
                command.Parameters["Value"] = values[0];
            }
            else
            {
                command.Parameters["Values"] = values;
            }

            return command;
        }

        var registers = request.Values.Count > 0
            ? request.Values.Select(value => Convert.ToUInt16(value)).ToArray()
            : ModbusValueConverter.EncodeRegisterValue(request.Value ?? 0, request.DataType, request.ByteOrder, request.WordOrder);

        command.CommandName = registers.Length == 1 ? "WriteSingleRegister" : "WriteMultipleRegisters";
        if (registers.Length == 1)
        {
            command.Parameters["Value"] = registers[0];
        }
        else
        {
            command.Parameters["Values"] = registers;
        }

        return command;
    }

    private static ModbusReadResult CreateReadResult(ModbusReadRequest request, CommandResult commandResult)
    {
        return new ModbusReadResult
        {
            ConnectionId = request.ConnectionId,
            RegisterType = request.RegisterType,
            Address = request.Address,
            Quantity = request.Quantity,
            DataType = request.DataType,
            Success = false,
            RequestFrame = commandResult.RequestData,
            ResponseFrame = commandResult.ResponseData,
            ElapsedTime = commandResult.ElapsedTime,
            Timestamp = DateTime.Now
        };
    }

    private static ModbusReadResult ReadFailure(ModbusReadRequest request, string errorMessage)
    {
        return new ModbusReadResult
        {
            ConnectionId = request.ConnectionId,
            RegisterType = request.RegisterType,
            Address = request.Address,
            Quantity = request.Quantity,
            DataType = request.DataType,
            Success = false,
            ErrorMessage = errorMessage,
            Timestamp = DateTime.Now
        };
    }

    private static ModbusOperationResult Failure(string connectionId, string errorMessage)
    {
        return new ModbusOperationResult
        {
            ConnectionId = connectionId,
            Success = false,
            ErrorMessage = errorMessage,
            Timestamp = DateTime.Now
        };
    }

    private static void PopulateReadValues(ModbusReadResult result, ModbusReadRequest request, byte[] payload)
    {
        if (request.RegisterType is ModbusRegisterType.Coil or ModbusRegisterType.DiscreteInput)
        {
            result.Values = ModbusValueConverter.DecodeCoils(payload, request.Quantity);
            result.Value = result.Values.Count == 1 ? result.Values[0] : null;
            return;
        }

        var byteCount = ModbusValueConverter.GetByteCount(request.DataType);
        if (payload.Length == byteCount)
        {
            result.Value = ModbusValueConverter.DecodeRegisterValue(payload, request.DataType, request.ByteOrder, request.WordOrder);
            result.Values.Add(result.Value);
            return;
        }

        for (var offset = 0; offset + byteCount <= payload.Length; offset += byteCount)
        {
            result.Values.Add(ModbusValueConverter.DecodeRegisterValue(payload.AsSpan(offset, byteCount), request.DataType, request.ByteOrder, request.WordOrder));
        }

        result.Value = result.Values.Count == 1 ? result.Values[0] : null;
    }

    private static ModbusResponseInfo TryReadResponsePayload(ProtocolType protocolType, byte[] response)
    {
        try
        {
            var functionCode = protocolType == ProtocolType.ModbusTcp ? response[7] : response[1];
            if (functionCode >= 0x80)
            {
                return ModbusResponseInfo.Exception(protocolType == ProtocolType.ModbusTcp ? response[8] : response[2]);
            }

            var isRead = functionCode is 0x01 or 0x02 or 0x03 or 0x04;
            if (!isRead)
            {
                return ModbusResponseInfo.Ok(Array.Empty<byte>());
            }

            var byteCountOffset = protocolType == ProtocolType.ModbusTcp ? 8 : 2;
            var payloadOffset = byteCountOffset + 1;
            var byteCount = response[byteCountOffset];
            if (payloadOffset + byteCount > response.Length)
            {
                return ModbusResponseInfo.Failure("Modbus response byte count exceeds frame length.");
            }

            return ModbusResponseInfo.Ok(response.Skip(payloadOffset).Take(byteCount).ToArray());
        }
        catch (Exception ex)
        {
            return ModbusResponseInfo.Failure(ex.Message);
        }
    }

    private sealed record ConnectionState(ModbusConnectionOptions Options, DeviceInfo Device, ProtocolConfig ProtocolConfig);

    private sealed class ModbusResponseInfo
    {
        public bool Success { get; private init; }
        public byte[] Payload { get; private init; } = Array.Empty<byte>();
        public byte? ExceptionCode { get; private init; }
        public string? ErrorMessage { get; private init; }

        public static ModbusResponseInfo Ok(byte[] payload) => new() { Success = true, Payload = payload };
        public static ModbusResponseInfo Exception(byte code) => new() { Success = true, ExceptionCode = code };
        public static ModbusResponseInfo Failure(string errorMessage) => new() { Success = false, ErrorMessage = errorMessage };
    }
}
