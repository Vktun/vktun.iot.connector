using System.Net.Sockets;
using Vktun.IoT.Connector.Configuration.Logging;
using Vktun.IoT.Connector.Configuration.Providers;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;
using Vktun.IoT.Connector.Protocol.Parsers;

namespace Vktun.IoT.Connector.Demo;

/// <summary>
/// Modbus TCP 通信测试 Demo
/// 演示如何通过 Modbus TCP 与 PLC 温湿度传感器通信
/// </summary>
public class ModbusTcpTest
{
    private static readonly ILogger _logger = new ConsoleLogger();
    private static TcpClient? _tcpClient;
    private static NetworkStream? _networkStream;
    private static readonly CancellationTokenSource _cts = new();
    private static ushort _transactionId = 0;

    /// <summary>
    /// 运行 Modbus TCP 测试
    /// </summary>
    public static async Task RunAsync(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("      Modbus TCP PLC 测试程序");
        Console.WriteLine("========================================");
        Console.WriteLine();

        try
        {
            // 1. 加载协议模板
            var protocolConfig = await LoadProtocolTemplateAsync();
            if (protocolConfig == null)
            {
                _logger.Error("加载协议模板失败，退出程序");
                return;
            }

            // 2. 配置 PLC 连接参数
            var plcConfig = GetPlcConfigFromArgs(args);

            // 3. 连接到 PLC
            if (!await ConnectToPlcAsync(plcConfig))
            {
                return;
            }

            // 4. 启动数据接收线程
            _ = Task.Run(() => ReceiveDataLoop(_cts.Token));

            // 5. 循环发送读取命令
            await PollingSensorDataAsync(protocolConfig, plcConfig);
        }
        catch (Exception ex)
        {
            _logger.Fatal($"程序运行异常: {ex.Message}", ex);
        }
        finally
        {
            _cts.Cancel();
            DisconnectFromPlc();
        }

        Console.WriteLine("\n按任意键退出...");
        Console.ReadKey();
    }

    /// <summary>
    /// 加载 PLC 温湿度传感器协议模板
    /// </summary>
    private static async Task<ModbusConfig?> LoadProtocolTemplateAsync()
    {
        try
        {
            var configProvider = new JsonConfigurationProvider(_logger);
            var templatePath = Path.Combine(AppContext.BaseDirectory, "Protocols", "PLC温湿度传感器协议.json");
            
            if (!File.Exists(templatePath))
            {
                templatePath = Path.Combine("Protocols", "PLC温湿度传感器协议.json");
                if (!File.Exists(templatePath))
                {
                    _logger.Error($"协议模板文件不存在: {templatePath}");
                    return null;
                }
            }

            var protocolConfig = await configProvider.LoadProtocolTemplateAsync(templatePath);
            if (protocolConfig == null)
            {
                return null;
            }

            var modbusConfig = new ModbusConfig
            {
                ProtocolId = protocolConfig.ProtocolId,
                ProtocolName = protocolConfig.ProtocolName,
                SlaveId = 1,
                ByteOrder = ByteOrder.BigEndian,
                WordOrder = WordOrder.HighWordFirst
            };

            _logger.Info($"成功加载协议模板: {protocolConfig.ProtocolName}");
            _logger.Info($"支持的测点数量: {protocolConfig.Points.Count}");
            
            return modbusConfig;
        }
        catch (Exception ex)
        {
            _logger.Error($"加载协议模板失败: {ex.Message}", ex);
            return null;
        }
    }

    /// <summary>
    /// 从命令行参数获取 PLC 配置
    /// </summary>
    private static PlcConfig GetPlcConfigFromArgs(string[] args)
    {
        var config = new PlcConfig
        {
            IpAddress = "192.168.1.100",
            Port = 502,
            SlaveId = 1,
            Timeout = 3000
        };

        if (args.Length > 0)
        {
            config.IpAddress = args[0];
        }

        if (args.Length > 1 && int.TryParse(args[1], out var port))
        {
            config.Port = port;
        }

        if (args.Length > 2 && byte.TryParse(args[2], out var slaveId))
        {
            config.SlaveId = slaveId;
        }

        _logger.Info($"PLC 配置: IP={config.IpAddress}, Port={config.Port}, SlaveId={config.SlaveId}");
        
        return config;
    }

    /// <summary>
    /// 连接到 PLC
    /// </summary>
    private static async Task<bool> ConnectToPlcAsync(PlcConfig config)
    {
        try
        {
            _logger.Info($"正在连接到 PLC: {config.IpAddress}:{config.Port}...");
            
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(config.IpAddress, config.Port);
            _networkStream = _tcpClient.GetStream();
            _networkStream.ReadTimeout = config.Timeout;
            _networkStream.WriteTimeout = config.Timeout;

            _logger.Info("PLC 连接成功");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"PLC 连接失败: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// 断开 PLC 连接
    /// </summary>
    private static void DisconnectFromPlc()
    {
        _networkStream?.Close();
        _networkStream?.Dispose();
        
        if (_tcpClient != null && _tcpClient.Connected)
        {
            _tcpClient.Close();
            _tcpClient.Dispose();
            _logger.Info("PLC 连接已断开");
        }
    }

    /// <summary>
    /// 数据接收循环
    /// </summary>
    private static async Task ReceiveDataLoop(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var parser = new ModbusTcpParser(_logger);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_networkStream == null || !_tcpClient?.Connected == true)
                {
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                if (_networkStream.DataAvailable)
                {
                    var bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead > 0)
                    {
                        var receivedData = new byte[bytesRead];
                        Array.Copy(buffer, receivedData, bytesRead);
                        
                        _logger.Debug($"收到数据: {BitConverter.ToString(receivedData).Replace('-', ' ')}");
                        
                        ParseModbusTcpResponse(receivedData);
                    }
                }
                else
                {
                    await Task.Delay(10, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException ex)
            {
                _logger.Error($"网络读取错误: {ex.Message}", ex);
                await Task.Delay(1000, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error($"接收数据异常: {ex.Message}", ex);
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 解析 Modbus TCP 响应数据
    /// </summary>
    private static void ParseModbusTcpResponse(byte[] frame)
    {
        try
        {
            if (frame.Length < 9)
            {
                _logger.Warning("响应帧长度不足，无法解析");
                return;
            }

            var transactionId = (ushort)((frame[0] << 8) | frame[1]);
            var protocolId = (ushort)((frame[2] << 8) | frame[3]);
            var length = (ushort)((frame[4] << 8) | frame[5]);
            var unitId = frame[6];
            var functionCode = frame[7];

            _logger.Debug($"MBAP Header: 事务ID={transactionId}, 协议ID={protocolId}, 长度={length}, 单元ID={unitId}");

            if (functionCode >= 0x80)
            {
                var errorCode = frame[8];
                _logger.Error($"Modbus 错误响应: 事务ID={transactionId}, 功能码={functionCode:X2}H, 错误码={errorCode:X2}H");
                return;
            }

            var byteCount = frame[8];
            var data = new byte[byteCount];
            Array.Copy(frame, 9, data, 0, byteCount);

            _logger.Info($"Modbus TCP 响应解析: 事务ID={transactionId}, 功能码={functionCode:X2}H, 数据长度={byteCount}字节");
            
            if (functionCode == 0x04)
            {
                ParseInputRegisters(data);
            }
            else if (functionCode == 0x03)
            {
                ParseHoldingRegisters(data);
            }
            else if (functionCode == 0x01)
            {
                ParseCoils(data);
            }
            else if (functionCode == 0x02)
            {
                ParseDiscreteInputs(data);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"解析 Modbus TCP 响应失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 解析输入寄存器数据（温湿度值）
    /// </summary>
    private static void ParseInputRegisters(byte[] data)
    {
        if (data.Length < 4) return;

        var tempRaw = (ushort)((data[0] << 8) | data[1]);
        var temperature = (short)tempRaw * 0.1;

        var humRaw = (ushort)((data[2] << 8) | data[3]);
        var humidity = humRaw * 0.1;

        _logger.Info("========================================");
        _logger.Info($"温度: {temperature:F1} ℃");
        _logger.Info($"湿度: {humidity:F1} %RH");
        
        if (data.Length >= 6)
        {
            var dewRaw = (ushort)((data[4] << 8) | data[5]);
            var dewPoint = (short)dewRaw * 0.1;
            _logger.Info($"露点: {dewPoint:F1} ℃");
        }
        _logger.Info("========================================");
    }

    /// <summary>
    /// 解析保持寄存器数据
    /// </summary>
    private static void ParseHoldingRegisters(byte[] data)
    {
        if (data.Length < 4) return;

        var tempRaw = (ushort)((data[0] << 8) | data[1]);
        var temperature = (short)tempRaw * 0.1;

        var humRaw = (ushort)((data[2] << 8) | data[3]);
        var humidity = humRaw * 0.1;

        _logger.Info("========================================");
        _logger.Info($"保持寄存器 - 温度: {temperature:F1} ℃");
        _logger.Info($"保持寄存器 - 湿度: {humidity:F1} %RH");

        if (data.Length >= 8)
        {
            var tempSetRaw = (ushort)((data[4] << 8) | data[5]);
            var tempSet = (short)tempSetRaw * 0.1;

            var humSetRaw = (ushort)((data[6] << 8) | data[7]);
            var humSet = humSetRaw * 0.1;

            _logger.Info($"保持寄存器 - 温度设定: {tempSet:F1} ℃");
            _logger.Info($"保持寄存器 - 湿度设定: {humSet:F1} %RH");
        }
        _logger.Info("========================================");
    }

    /// <summary>
    /// 解析线圈状态
    /// </summary>
    private static void ParseCoils(byte[] data)
    {
        if (data.Length < 1) return;

        var deviceStatus = (data[0] & 0x01) != 0;
        _logger.Info($"设备运行状态: {(deviceStatus ? "运行中" : "已停止")}");
    }

    /// <summary>
    /// 解析离散输入状态
    /// </summary>
    private static void ParseDiscreteInputs(byte[] data)
    {
        if (data.Length < 1) return;

        var alarmStatus = (data[0] & 0x01) != 0;
        _logger.Info($"报警状态: {(alarmStatus ? "报警中" : "正常")}");
    }

    /// <summary>
    /// 轮询传感器数据
    /// </summary>
    private static async Task PollingSensorDataAsync(ModbusConfig config, PlcConfig plcConfig)
    {
        _logger.Info("\n开始轮询传感器数据...");
        _logger.Info("按 Ctrl+C 停止程序");

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await SendModbusTcpCommandAsync(0x03, 0, 4, plcConfig.SlaveId);
                await Task.Delay(1000);

                await SendModbusTcpCommandAsync(0x01, 0, 1, plcConfig.SlaveId);
                await Task.Delay(1000);

                await SendModbusTcpCommandAsync(0x02, 0, 1, plcConfig.SlaveId);
                await Task.Delay(1000);

                await SendModbusTcpCommandAsync(0x03, 20, 2, plcConfig.SlaveId);
                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                _logger.Error($"轮询数据失败: {ex.Message}", ex);
                await Task.Delay(2000);
            }
        }
    }

    /// <summary>
    /// 发送 Modbus TCP 命令
    /// </summary>
    private static async Task SendModbusTcpCommandAsync(byte functionCode, ushort startAddress, ushort quantity, byte slaveId)
    {
        if (_networkStream == null || !_tcpClient?.Connected == true)
        {
            _logger.Warning("PLC 未连接，无法发送命令");
            return;
        }

        try
        {
            _transactionId++;

            var frame = new List<byte>();

            frame.Add((byte)(_transactionId >> 8));
            frame.Add((byte)(_transactionId & 0xFF));
            frame.Add(0x00);
            frame.Add(0x00);
            frame.Add(0x00);
            frame.Add(0x06);
            frame.Add(slaveId);
            frame.Add(functionCode);
            frame.Add((byte)(startAddress >> 8));
            frame.Add((byte)(startAddress & 0xFF));
            frame.Add((byte)(quantity >> 8));
            frame.Add((byte)(quantity & 0xFF));

            var commandBytes = frame.ToArray();
            _logger.Debug($"发送命令: {BitConverter.ToString(commandBytes).Replace('-', ' ')}");

            await _networkStream.WriteAsync(commandBytes, 0, commandBytes.Length);
            
            var functionName = GetFunctionName(functionCode);
            _logger.Info($"已发送 {functionName} 命令 (事务ID: {_transactionId}, 起始地址: {startAddress}, 数量: {quantity})");
        }
        catch (Exception ex)
        {
            _logger.Error($"发送 Modbus TCP 命令失败: {ex.Message}", ex);
            throw;
        }
    }

    /// <summary>
    /// 获取功能码名称
    /// </summary>
    private static string GetFunctionName(byte functionCode)
    {
        return functionCode switch
        {
            0x01 => "读取线圈",
            0x02 => "读取离散输入",
            0x03 => "读取保持寄存器",
            0x04 => "读取输入寄存器",
            0x05 => "写单个线圈",
            0x06 => "写单个寄存器",
            0x0F => "写多个线圈",
            0x10 => "写多个寄存器",
            _ => $"功能码 {functionCode:X2}H"
        };
    }
}

/// <summary>
/// PLC 配置
/// </summary>
public class PlcConfig
{
    public string IpAddress { get; set; } = "192.168.1.100";
    public int Port { get; set; } = 502;
    public byte SlaveId { get; set; } = 1;
    public int Timeout { get; set; } = 3000;
}