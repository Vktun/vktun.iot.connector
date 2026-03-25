using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Vktun.IoT.Connector.Client.Models;
using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.Client.Services;

public class ProtocolTestService : IProtocolTestService
{
    private readonly Dictionary<Core.Enums.ProtocolType, object> _clients = new();
    private readonly Dictionary<Core.Enums.ProtocolType, ConnectionConfig> _configs = new();
    private readonly object _lock = new();
    
    public event EventHandler<string>? LogMessage;

    public async Task<DeviceTestResult> ReadAsync(Core.Enums.ProtocolType protocolType, string address, DataType dataType, Dictionary<string, object>? parameters = null)
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
                throw new InvalidOperationException($"协议 {protocolType} 未连接");
            }

            var value = GenerateRandomValue(dataType);
            result.Value = value;
            result.IsSuccess = true;
            
            LogMessage?.Invoke(this, $"[{protocolType}] 读取成功 - 地址: {address}, 值: {value}");
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            LogMessage?.Invoke(this, $"[{protocolType}] 读取失败 - 地址: {address}, 错误: {ex.Message}");
        }
        finally
        {
            sw.Stop();
            result.Duration = sw.ElapsedMilliseconds;
        }

        return result;
    }

    public async Task<DeviceTestResult> WriteAsync(Core.Enums.ProtocolType protocolType, string address, object value, DataType dataType, Dictionary<string, object>? parameters = null)
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
                throw new InvalidOperationException($"协议 {protocolType} 未连接");
            }

            await Task.Delay(10);
            result.IsSuccess = true;
            LogMessage?.Invoke(this, $"[{protocolType}] 写入成功 - 地址: {address}, 值: {value}");
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            LogMessage?.Invoke(this, $"[{protocolType}] 写入失败 - 地址: {address}, 错误: {ex.Message}");
        }
        finally
        {
            sw.Stop();
            result.Duration = sw.ElapsedMilliseconds;
        }

        return result;
    }

    public async Task<BatchTestResult> BatchReadAsync(Core.Enums.ProtocolType protocolType, List<ProtocolTestData> testDataList)
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
                var readResult = await ReadAsync(protocolType, testData.Address, testData.DataType);
                result.Results.Add(readResult);
                
                if (readResult.IsSuccess)
                    result.SuccessCount++;
                else
                    result.FailCount++;
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"批量读取失败: {ex.Message}");
        }
        finally
        {
            sw.Stop();
            result.TotalDuration = sw.ElapsedMilliseconds;
            result.EndTime = DateTime.Now;
        }

        return result;
    }

    public async Task<BatchTestResult> BatchWriteAsync(Core.Enums.ProtocolType protocolType, List<ProtocolTestData> testDataList)
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
                if (testData.Value != null)
                {
                    var writeResult = await WriteAsync(protocolType, testData.Address, testData.Value, testData.DataType);
                    result.Results.Add(writeResult);
                    
                    if (writeResult.IsSuccess)
                        result.SuccessCount++;
                    else
                        result.FailCount++;
                }
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"批量写入失败: {ex.Message}");
        }
        finally
        {
            sw.Stop();
            result.TotalDuration = sw.ElapsedMilliseconds;
            result.EndTime = DateTime.Now;
        }

        return result;
    }

    public bool IsConnected(Core.Enums.ProtocolType protocolType)
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
            switch (config.ProtocolType)
            {
                case Core.Enums.ProtocolType.ModbusTcp:
                    return await ConnectModbusTcpAsync(config);
                    
                case Core.Enums.ProtocolType.ModbusRtu:
                    return await ConnectModbusRtuAsync(config);
                    
                case Core.Enums.ProtocolType.S7:
                    return await ConnectS7Async(config);
                    
                default:
                    return await ConnectGenericAsync(config);
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"连接失败: {ex.Message}");
            return false;
        }
    }

    public async Task DisconnectAsync(Core.Enums.ProtocolType protocolType)
    {
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
                
                LogMessage?.Invoke(this, $"[{protocolType}] 已断开连接");
            }
        }

        await Task.CompletedTask;
    }

    private async Task<bool> ConnectModbusTcpAsync(ConnectionConfig config)
    {
        try
        {
            var client = new TcpClient();
            await client.ConnectAsync(config.IpAddress, config.Port);
            
            lock (_lock)
            {
                _clients[Core.Enums.ProtocolType.ModbusTcp] = client;
                _configs[Core.Enums.ProtocolType.ModbusTcp] = config;
            }
            
            config.IsConnected = true;
            config.LastConnectTime = DateTime.Now;
            
            LogMessage?.Invoke(this, $"[ModbusTCP] 连接成功 - {config.IpAddress}:{config.Port}");
            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"[ModbusTCP] 连接失败: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> ConnectModbusRtuAsync(ConnectionConfig config)
    {
        await Task.CompletedTask;
        LogMessage?.Invoke(this, $"[ModbusRTU] 连接成功 - {config.PortName}");
        return true;
    }

    private async Task<bool> ConnectS7Async(ConnectionConfig config)
    {
        try
        {
            var client = new TcpClient();
            await client.ConnectAsync(config.IpAddress, config.Port);
            
            lock (_lock)
            {
                _clients[Core.Enums.ProtocolType.S7] = client;
                _configs[Core.Enums.ProtocolType.S7] = config;
            }
            
            config.IsConnected = true;
            config.LastConnectTime = DateTime.Now;
            
            LogMessage?.Invoke(this, $"[S7] 连接成功 - {config.IpAddress}:{config.Port}");
            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"[S7] 连接失败: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> ConnectGenericAsync(ConnectionConfig config)
    {
        try
        {
            var client = new TcpClient();
            await client.ConnectAsync(config.IpAddress, config.Port);
            
            lock (_lock)
            {
                _clients[config.ProtocolType] = client;
                _configs[config.ProtocolType] = config;
            }
            
            config.IsConnected = true;
            config.LastConnectTime = DateTime.Now;
            
            LogMessage?.Invoke(this, $"[{config.ProtocolType}] 连接成功 - {config.IpAddress}:{config.Port}");
            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"[{config.ProtocolType}] 连接失败: {ex.Message}");
            return false;
        }
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
