using System.Diagnostics;
using Vktun.IoT.Connector.Client.Models;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;
using ClientParity = Vktun.IoT.Connector.Client.Models.Parity;
using ClientStopBits = Vktun.IoT.Connector.Client.Models.StopBits;
using CoreModbusRegisterType = Vktun.IoT.Connector.Core.Models.ModbusRegisterType;
using TcpClient = System.Net.Sockets.TcpClient;

namespace Vktun.IoT.Connector.Client.Services;

public class ProtocolTestService : IProtocolTestService
{
    private readonly IModbusClient _modbusClient;
    private readonly Dictionary<ProtocolType, object> _clients = new();
    private readonly Dictionary<ProtocolType, ConnectionConfig> _configs = new();
    private readonly object _lock = new();

    public ProtocolTestService(IModbusClient modbusClient)
    {
        _modbusClient = modbusClient;
    }

    public event EventHandler<string>? LogMessage;

    public async Task<DeviceTestResult> ReadAsync(
        ProtocolType protocolType,
        string address,
        DataType dataType,
        Dictionary<string, object>? parameters = null)
    {
        if (IsModbus(protocolType))
        {
            return await ReadModbusAsync(protocolType, address, dataType, parameters).ConfigureAwait(false);
        }

        return await ReadLegacyAsync(protocolType, address, dataType).ConfigureAwait(false);
    }

    public async Task<DeviceTestResult> WriteAsync(
        ProtocolType protocolType,
        string address,
        object value,
        DataType dataType,
        Dictionary<string, object>? parameters = null)
    {
        if (IsModbus(protocolType))
        {
            return await WriteModbusAsync(protocolType, address, value, dataType, parameters).ConfigureAwait(false);
        }

        return await WriteLegacyAsync(protocolType, address, value, dataType).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<DeviceTestResult> PollAsync(
        ProtocolType protocolType,
        string address,
        DataType dataType,
        int intervalMs,
        Dictionary<string, object>? parameters = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!IsModbus(protocolType))
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                yield return await ReadAsync(protocolType, address, dataType, parameters).ConfigureAwait(false);
                await Task.Delay(Math.Max(1, intervalMs), cancellationToken).ConfigureAwait(false);
            }

            yield break;
        }

        var request = CreateReadRequest(protocolType, address, dataType, parameters);
        await foreach (var result in _modbusClient.PollAsync(new ModbusPollRequest
        {
            ReadRequest = request,
            IntervalMs = intervalMs
        }, cancellationToken).ConfigureAwait(false))
        {
            yield return ToDeviceTestResult(result);
        }
    }

    public async Task<BatchTestResult> BatchReadAsync(ProtocolType protocolType, List<ProtocolTestData> testDataList)
    {
        var result = new BatchTestResult
        {
            StartTime = DateTime.Now
        };

        var sw = Stopwatch.StartNew();

        try
        {
            foreach (var testData in testDataList)
            {
                var readResult = await ReadAsync(protocolType, testData.Address, testData.DataType, CreateParameters(testData)).ConfigureAwait(false);
                result.Results.Add(readResult);

                if (readResult.IsSuccess)
                {
                    result.SuccessCount++;
                }
                else
                {
                    result.FailCount++;
                }
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"Batch read failed: {ex.Message}");
        }
        finally
        {
            sw.Stop();
            result.TotalDuration = sw.ElapsedMilliseconds;
            result.EndTime = DateTime.Now;
        }

        return result;
    }

    public async Task<BatchTestResult> BatchWriteAsync(ProtocolType protocolType, List<ProtocolTestData> testDataList)
    {
        var result = new BatchTestResult
        {
            StartTime = DateTime.Now
        };

        var sw = Stopwatch.StartNew();

        try
        {
            foreach (var testData in testDataList)
            {
                if (testData.Value == null)
                {
                    continue;
                }

                var writeResult = await WriteAsync(
                    protocolType,
                    testData.Address,
                    testData.Value,
                    testData.DataType,
                    CreateParameters(testData)).ConfigureAwait(false);
                result.Results.Add(writeResult);

                if (writeResult.IsSuccess)
                {
                    result.SuccessCount++;
                }
                else
                {
                    result.FailCount++;
                }
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"Batch write failed: {ex.Message}");
        }
        finally
        {
            sw.Stop();
            result.TotalDuration = sw.ElapsedMilliseconds;
            result.EndTime = DateTime.Now;
        }

        return result;
    }

    public bool IsConnected(ProtocolType protocolType)
    {
        lock (_lock)
        {
            return _clients.ContainsKey(protocolType);
        }
    }

    public async Task<bool> ConnectAsync(ConnectionConfig config)
    {
        try
        {
            return config.ProtocolType switch
            {
                ProtocolType.ModbusTcp => await ConnectModbusTcpAsync(config).ConfigureAwait(false),
                ProtocolType.ModbusRtu => await ConnectModbusRtuAsync(config).ConfigureAwait(false),
                ProtocolType.S7 => await ConnectS7Async(config).ConfigureAwait(false),
                _ => await ConnectGenericAsync(config).ConfigureAwait(false)
            };
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"Connect failed: {ex.Message}");
            return false;
        }
    }

    public async Task DisconnectAsync(ProtocolType protocolType)
    {
        if (IsModbus(protocolType))
        {
            var sdkResult = await _modbusClient.DisconnectAsync(GetConnectionId(protocolType)).ConfigureAwait(false);
            lock (_lock)
            {
                _clients.Remove(protocolType);
                _configs.Remove(protocolType);
            }

            LogMessage?.Invoke(this, sdkResult.Success
                ? $"[{protocolType}] disconnected"
                : $"[{protocolType}] disconnect failed: {sdkResult.ErrorMessage}");
            return;
        }

        lock (_lock)
        {
            if (_clients.TryGetValue(protocolType, out var client))
            {
                if (client is TcpClient tcpClient)
                {
                    tcpClient.Close();
                    tcpClient.Dispose();
                }

                _clients.Remove(protocolType);
                _configs.Remove(protocolType);

                LogMessage?.Invoke(this, $"[{protocolType}] disconnected");
            }
        }
    }

    private async Task<DeviceTestResult> ReadModbusAsync(
        ProtocolType protocolType,
        string address,
        DataType dataType,
        Dictionary<string, object>? parameters)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var request = CreateReadRequest(protocolType, address, dataType, parameters);
            var result = await _modbusClient.ReadAsync(request).ConfigureAwait(false);
            var testResult = ToDeviceTestResult(result);
            LogMessage?.Invoke(this, testResult.IsSuccess
                ? $"[{protocolType}] read OK address={address} value={FormatValue(testResult.Value)} tx={testResult.Request} rx={testResult.Response}"
                : $"[{protocolType}] read failed address={address} error={testResult.ErrorMessage} tx={testResult.Request} rx={testResult.Response}");
            return testResult;
        }
        catch (Exception ex)
        {
            return FailResult(address, dataType, sw, ex.Message);
        }
    }

    private async Task<DeviceTestResult> WriteModbusAsync(
        ProtocolType protocolType,
        string address,
        object value,
        DataType dataType,
        Dictionary<string, object>? parameters)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var request = CreateWriteRequest(protocolType, address, value, dataType, parameters);
            var result = await _modbusClient.WriteAsync(request).ConfigureAwait(false);
            var testResult = ToDeviceTestResult(result, address, value, dataType);
            LogMessage?.Invoke(this, testResult.IsSuccess
                ? $"[{protocolType}] write OK address={address} value={FormatValue(value)} tx={testResult.Request} rx={testResult.Response}"
                : $"[{protocolType}] write failed address={address} error={testResult.ErrorMessage} tx={testResult.Request} rx={testResult.Response}");
            return testResult;
        }
        catch (Exception ex)
        {
            return FailResult(address, dataType, sw, ex.Message);
        }
    }

    private async Task<DeviceTestResult> ReadLegacyAsync(ProtocolType protocolType, string address, DataType dataType)
    {
        var result = new DeviceTestResult
        {
            Address = address,
            DataType = dataType.ToString(),
            Timestamp = DateTime.Now
        };

        var sw = Stopwatch.StartNew();
        try
        {
            if (!IsConnected(protocolType))
            {
                throw new InvalidOperationException($"Protocol {protocolType} is not connected.");
            }

            var value = GenerateRandomValue(dataType);
            result.Value = value;
            result.IsSuccess = true;
            LogMessage?.Invoke(this, $"[{protocolType}] read OK address={address} value={FormatValue(value)}");
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            LogMessage?.Invoke(this, $"[{protocolType}] read failed address={address} error={ex.Message}");
        }
        finally
        {
            sw.Stop();
            result.Duration = sw.ElapsedMilliseconds;
        }

        return await Task.FromResult(result).ConfigureAwait(false);
    }

    private async Task<DeviceTestResult> WriteLegacyAsync(ProtocolType protocolType, string address, object value, DataType dataType)
    {
        var result = new DeviceTestResult
        {
            Address = address,
            DataType = dataType.ToString(),
            Value = value,
            Timestamp = DateTime.Now
        };

        var sw = Stopwatch.StartNew();
        try
        {
            if (!IsConnected(protocolType))
            {
                throw new InvalidOperationException($"Protocol {protocolType} is not connected.");
            }

            await Task.Delay(10).ConfigureAwait(false);
            result.IsSuccess = true;
            LogMessage?.Invoke(this, $"[{protocolType}] write OK address={address} value={FormatValue(value)}");
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            LogMessage?.Invoke(this, $"[{protocolType}] write failed address={address} error={ex.Message}");
        }
        finally
        {
            sw.Stop();
            result.Duration = sw.ElapsedMilliseconds;
        }

        return result;
    }

    private async Task<bool> ConnectModbusTcpAsync(ConnectionConfig config)
    {
        if (IsConnected(ProtocolType.ModbusTcp))
        {
            await DisconnectAsync(ProtocolType.ModbusTcp).ConfigureAwait(false);
        }

        var result = await _modbusClient.ConnectAsync(new ModbusConnectionOptions
        {
            ConnectionId = GetConnectionId(ProtocolType.ModbusTcp),
            ProtocolType = ProtocolType.ModbusTcp,
            IpAddress = config.IpAddress,
            Port = config.Port,
            SlaveId = config.SlaveId,
            Timeout = config.Timeout
        }).ConfigureAwait(false);

        if (!result.Success)
        {
            LogMessage?.Invoke(this, $"[ModbusTcp] connect failed: {result.ErrorMessage}");
            return false;
        }

        StoreConnectedConfig(ProtocolType.ModbusTcp, config);
        LogMessage?.Invoke(this, $"[ModbusTcp] connected {config.IpAddress}:{config.Port}");
        return true;
    }

    private async Task<bool> ConnectModbusRtuAsync(ConnectionConfig config)
    {
        if (IsConnected(ProtocolType.ModbusRtu))
        {
            await DisconnectAsync(ProtocolType.ModbusRtu).ConfigureAwait(false);
        }

        var result = await _modbusClient.ConnectAsync(new ModbusConnectionOptions
        {
            ConnectionId = GetConnectionId(ProtocolType.ModbusRtu),
            ProtocolType = ProtocolType.ModbusRtu,
            PortName = config.PortName,
            BaudRate = config.BaudRate,
            DataBits = config.DataBits,
            Parity = MapParity(config.Parity),
            StopBits = MapStopBits(config.StopBits),
            SlaveId = config.SlaveId,
            Timeout = config.Timeout
        }).ConfigureAwait(false);

        if (!result.Success)
        {
            LogMessage?.Invoke(this, $"[ModbusRtu] connect failed: {result.ErrorMessage}");
            return false;
        }

        StoreConnectedConfig(ProtocolType.ModbusRtu, config);
        LogMessage?.Invoke(this, $"[ModbusRtu] connected {config.PortName} {config.BaudRate},{config.DataBits},{config.Parity},{config.StopBits}");
        return true;
    }

    private async Task<bool> ConnectS7Async(ConnectionConfig config)
    {
        try
        {
            var client = new TcpClient();
            await client.ConnectAsync(config.IpAddress, config.Port).ConfigureAwait(false);
            StoreTcpClient(ProtocolType.S7, config, client);
            LogMessage?.Invoke(this, $"[S7] connected {config.IpAddress}:{config.Port}");
            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"[S7] connect failed: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> ConnectGenericAsync(ConnectionConfig config)
    {
        try
        {
            var client = new TcpClient();
            await client.ConnectAsync(config.IpAddress, config.Port).ConfigureAwait(false);
            StoreTcpClient(config.ProtocolType, config, client);
            LogMessage?.Invoke(this, $"[{config.ProtocolType}] connected {config.IpAddress}:{config.Port}");
            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"[{config.ProtocolType}] connect failed: {ex.Message}");
            return false;
        }
    }

    private ModbusReadRequest CreateReadRequest(
        ProtocolType protocolType,
        string address,
        DataType dataType,
        Dictionary<string, object>? parameters)
    {
        var registerType = GetRegisterType(parameters);
        return new ModbusReadRequest
        {
            ConnectionId = GetConnectionId(protocolType),
            RegisterType = registerType,
            Address = ParseAddress(address),
            Quantity = GetQuantity(parameters, registerType, dataType),
            DataType = dataType,
            Timeout = GetTimeout(protocolType)
        };
    }

    private ModbusWriteRequest CreateWriteRequest(
        ProtocolType protocolType,
        string address,
        object value,
        DataType dataType,
        Dictionary<string, object>? parameters)
    {
        var request = new ModbusWriteRequest
        {
            ConnectionId = GetConnectionId(protocolType),
            RegisterType = GetRegisterType(parameters),
            Address = ParseAddress(address),
            DataType = dataType,
            Timeout = GetTimeout(protocolType)
        };

        if (value is string text && text.Contains(',', StringComparison.Ordinal))
        {
            request.Values = text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => (object?)ConvertString(item, dataType))
                .ToList();
        }
        else
        {
            request.Value = value is string stringValue ? ConvertString(stringValue, dataType) : value;
        }

        return request;
    }

    private static DeviceTestResult ToDeviceTestResult(ModbusReadResult result)
    {
        return new DeviceTestResult
        {
            Address = result.Address.ToString(),
            Value = result.Value ?? (result.Values.Count > 0 ? string.Join(", ", result.Values.Select(FormatValue)) : null),
            DataType = result.DataType.ToString(),
            IsSuccess = result.Success,
            ErrorMessage = result.ErrorMessage,
            Timestamp = result.Timestamp,
            Request = ToHex(result.RequestFrame),
            Response = ToHex(result.ResponseFrame),
            Duration = result.ElapsedTime.TotalMilliseconds
        };
    }

    private static DeviceTestResult ToDeviceTestResult(ModbusOperationResult result, string address, object value, DataType dataType)
    {
        return new DeviceTestResult
        {
            Address = address,
            Value = value,
            DataType = dataType.ToString(),
            IsSuccess = result.Success,
            ErrorMessage = result.ErrorMessage,
            Timestamp = result.Timestamp,
            Request = ToHex(result.RequestFrame),
            Response = ToHex(result.ResponseFrame),
            Duration = result.ElapsedTime.TotalMilliseconds
        };
    }

    private static DeviceTestResult FailResult(string address, DataType dataType, Stopwatch sw, string errorMessage)
    {
        sw.Stop();
        return new DeviceTestResult
        {
            Address = address,
            DataType = dataType.ToString(),
            IsSuccess = false,
            ErrorMessage = errorMessage,
            Timestamp = DateTime.Now,
            Duration = sw.ElapsedMilliseconds
        };
    }

    private void StoreConnectedConfig(ProtocolType protocolType, ConnectionConfig config)
    {
        config.IsConnected = true;
        config.LastConnectTime = DateTime.Now;
        lock (_lock)
        {
            _clients[protocolType] = _modbusClient;
            _configs[protocolType] = config;
        }
    }

    private void StoreTcpClient(ProtocolType protocolType, ConnectionConfig config, TcpClient client)
    {
        config.IsConnected = true;
        config.LastConnectTime = DateTime.Now;
        lock (_lock)
        {
            _clients[protocolType] = client;
            _configs[protocolType] = config;
        }
    }

    private int GetTimeout(ProtocolType protocolType)
    {
        lock (_lock)
        {
            return _configs.TryGetValue(protocolType, out var config) ? config.Timeout : 3000;
        }
    }

    private static Dictionary<string, object> CreateParameters(ProtocolTestData testData)
    {
        var parameters = new Dictionary<string, object>();
        if (testData is ModbusTestData modbusTestData)
        {
            parameters["RegisterType"] = modbusTestData.RegisterType.ToString();
            parameters["Quantity"] = modbusTestData.Quantity;
        }

        return parameters;
    }

    private static CoreModbusRegisterType GetRegisterType(Dictionary<string, object>? parameters)
    {
        if (parameters == null || !parameters.TryGetValue("RegisterType", out var raw) || raw == null)
        {
            return CoreModbusRegisterType.HoldingRegister;
        }

        return raw switch
        {
            CoreModbusRegisterType registerType => registerType,
            string text => Enum.Parse<CoreModbusRegisterType>(text),
            _ => Enum.Parse<CoreModbusRegisterType>(raw.ToString() ?? nameof(CoreModbusRegisterType.HoldingRegister))
        };
    }

    private static ushort GetQuantity(Dictionary<string, object>? parameters, CoreModbusRegisterType registerType, DataType dataType)
    {
        var minimum = registerType is CoreModbusRegisterType.Coil or CoreModbusRegisterType.DiscreteInput
            ? (ushort)1
            : (ushort)ModbusValueConverter.GetRegisterCount(dataType);

        if (parameters != null &&
            parameters.TryGetValue("Quantity", out var raw) &&
            ushort.TryParse(raw?.ToString(), out var quantity) &&
            quantity > 0)
        {
            return Math.Max(quantity, minimum);
        }

        return minimum;
    }

    private static ushort ParseAddress(string address)
    {
        if (!ushort.TryParse(address, out var parsed))
        {
            throw new ArgumentException("Address must be a zero-based ushort value.", nameof(address));
        }

        return parsed;
    }

    private static object ConvertString(string value, DataType dataType)
    {
        return dataType switch
        {
            DataType.Bool or DataType.Bit => bool.Parse(value),
            DataType.Int8 => sbyte.Parse(value),
            DataType.UInt8 => byte.Parse(value),
            DataType.Int16 => short.Parse(value),
            DataType.UInt16 => ushort.Parse(value),
            DataType.Int32 => int.Parse(value),
            DataType.UInt32 => uint.Parse(value),
            DataType.Int64 => long.Parse(value),
            DataType.UInt64 => ulong.Parse(value),
            DataType.Float => float.Parse(value),
            DataType.Double => double.Parse(value),
            _ => value
        };
    }

    private static string ToHex(byte[]? frame)
    {
        return frame is { Length: > 0 }
            ? BitConverter.ToString(frame).Replace("-", " ")
            : string.Empty;
    }

    private static string FormatValue(object? value)
    {
        return value?.ToString() ?? string.Empty;
    }

    private static string GetConnectionId(ProtocolType protocolType)
    {
        return protocolType.ToString();
    }

    private static bool IsModbus(ProtocolType protocolType)
    {
        return protocolType is ProtocolType.ModbusTcp or ProtocolType.ModbusRtu;
    }

    private static SerialParity MapParity(ClientParity parity)
    {
        return parity switch
        {
            ClientParity.Odd => SerialParity.Odd,
            ClientParity.Even => SerialParity.Even,
            ClientParity.Mark => SerialParity.Mark,
            ClientParity.Space => SerialParity.Space,
            _ => SerialParity.None
        };
    }

    private static SerialStopBits MapStopBits(ClientStopBits stopBits)
    {
        return stopBits switch
        {
            ClientStopBits.OnePointFive => SerialStopBits.OnePointFive,
            ClientStopBits.Two => SerialStopBits.Two,
            _ => SerialStopBits.One
        };
    }

    private static object GenerateRandomValue(DataType dataType)
    {
        var random = new Random();
        return dataType switch
        {
            DataType.Int8 => (sbyte)random.Next(-128, 127),
            DataType.UInt8 => (byte)random.Next(0, 255),
            DataType.Int16 => (short)random.Next(-32768, 32767),
            DataType.UInt16 => (ushort)random.Next(0, 65535),
            DataType.Int32 => random.Next(),
            DataType.UInt32 => (uint)random.Next(),
            DataType.Int64 => (long)random.Next(),
            DataType.UInt64 => (ulong)random.Next(),
            DataType.Float => (float)(random.NextDouble() * 1000),
            DataType.Double => random.NextDouble() * 10000,
            _ => 0
        };
    }
}
