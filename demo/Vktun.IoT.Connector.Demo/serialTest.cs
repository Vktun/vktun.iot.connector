using System.IO.Ports;
using Vktun.IoT.Connector.Business.Managers;
using Vktun.IoT.Connector.Configuration.Logging;
using Vktun.IoT.Connector.Configuration.Providers;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;
using Vktun.IoT.Connector.Protocol.Factories;
using Vktun.IoT.Connector.Protocol.Parsers;

namespace Vktun.IoT.Connector.Demo;

/// <summary>
/// 串口通信测试 Demo
/// 演示如何通过串口与 Modbus RTU 温湿度传感器通信
/// </summary>
public class SerialPortTest
{
    private static readonly ILogger _logger = new ConsoleLogger();
    private static SerialPort? _serialPort;
    private static readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// 运行串口测试
    /// </summary>
    public static async Task RunAsync(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("      串口温湿度传感器测试程序");
        Console.WriteLine("========================================");
        Console.WriteLine();

        try
        {
            // 1. 列出可用串口
            ListAvailablePorts();

            // 2. 加载协议模板
            var protocolConfig = await LoadProtocolTemplateAsync();
            if (protocolConfig == null)
            {
                _logger.Error("加载协议模板失败，退出程序");
                return;
            }

            // 3. 配置串口参数
            var serialConfig = GetSerialConfigFromArgs(args);

            // 4. 打开串口
            if (!OpenSerialPort(serialConfig))
            {
                return;
            }

            // 5. 启动数据接收线程
            _ = Task.Run(() => ReceiveDataLoop(_cts.Token));

            // 6. 循环发送读取命令
            await PollingSensorDataAsync(protocolConfig, serialConfig);
        }
        catch (Exception ex)
        {
            _logger.Fatal($"程序运行异常: {ex.Message}", ex);
        }
        finally
        {
            _cts.Cancel();
            CloseSerialPort();
        }

        Console.WriteLine("\n按任意键退出...");
        Console.ReadKey();
    }

    /// <summary>
    /// 列出系统可用串口
    /// </summary>
    private static void ListAvailablePorts()
    {
        _logger.Info("系统可用串口列表:");
        var ports = SerialPort.GetPortNames();
        
        if (ports.Length == 0)
        {
            _logger.Warning("未检测到可用串口！");
            return;
        }

        foreach (var port in ports)
        {
            _logger.Info($"  - {port}");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// 加载温湿度传感器协议模板
    /// </summary>
    private static async Task<ModbusConfig?> LoadProtocolTemplateAsync()
    {
        try
        {
            var configProvider = new JsonConfigurationProvider(_logger);
            var templatePath = Path.Combine(AppContext.BaseDirectory, "Protocols", "温湿度传感器协议.json");
            
            if (!File.Exists(templatePath))
            {
                // 尝试从项目目录加载
                templatePath = Path.Combine("Protocols", "温湿度传感器协议.json");
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

            // 转换为 ModbusConfig
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
    /// 从命令行参数获取串口配置
    /// </summary>
    private static SerialPortTestConfig GetSerialConfigFromArgs(string[] args)
    {
        var config = new SerialPortTestConfig
        {
            PortName = "COM1",
            BaudRate = 9600,
            DataBits = 8,
            StopBits = StopBits.One,
            Parity = Parity.None,
            ReadTimeout = 1000,
            WriteTimeout = 1000
        };

        if (args.Length > 0)
        {
            config.PortName = args[0];
        }

        if (args.Length > 1 && int.TryParse(args[1], out var baudRate))
        {
            config.BaudRate = baudRate;
        }

        _logger.Info($"串口配置: {config.PortName}, {config.BaudRate} baud, {config.DataBits} data bits, {config.StopBits}, {config.Parity} parity");
        
        return config;
    }

    /// <summary>
    /// 打开串口
    /// </summary>
    private static bool OpenSerialPort(SerialPortTestConfig config)
    {
        try
        {
            _serialPort = new SerialPort
            {
                PortName = config.PortName,
                BaudRate = config.BaudRate,
                DataBits = config.DataBits,
                StopBits = config.StopBits,
                Parity = config.Parity,
                ReadTimeout = config.ReadTimeout,
                WriteTimeout = config.WriteTimeout
            };

            _serialPort.Open();
            _logger.Info($"串口 {config.PortName} 打开成功");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"串口打开失败: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// 关闭串口
    /// </summary>
    private static void CloseSerialPort()
    {
        if (_serialPort != null && _serialPort.IsOpen)
        {
            _serialPort.Close();
            _logger.Info("串口已关闭");
        }
    }

    /// <summary>
    /// 数据接收循环
    /// </summary>
    private static async Task ReceiveDataLoop(CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        var dataBuffer = new List<byte>();
        var parser = new ModbusRtuParser(_logger);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_serialPort == null || !_serialPort.IsOpen)
                {
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                if (_serialPort.BytesToRead > 0)
                {
                    var bytesRead = await _serialPort.BaseStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead > 0)
                    {
                        var receivedData = new byte[bytesRead];
                        Array.Copy(buffer, receivedData, bytesRead);
                        
                        _logger.Debug($"收到数据: {BitConverter.ToString(receivedData).Replace('-', ' ')}");
                        
                        dataBuffer.AddRange(receivedData);

                        // 尝试解析 Modbus RTU 响应
                        while (dataBuffer.Count >= 5) // 最小 Modbus RTU 响应长度
                        {
                            // 检查帧完整性（简单检查：地址 + 功能码 + 数据长度 + CRC）
                            var potentialLength = 3 + dataBuffer[2] + 2; // 地址(1) + 功能码(1) + 长度(1) + 数据(N) + CRC(2)
                            
                            if (dataBuffer.Count >= potentialLength)
                            {
                                var frame = dataBuffer.Take(potentialLength).ToArray();
                                
                                // CRC 校验
                                var isValid = CrcCalculator.VerifyCrc16Modbus(frame, 0);
                                if (isValid)
                                {
                                    _logger.Info("收到有效的 Modbus RTU 响应帧");
                                    ParseModbusResponse(frame);
                                    dataBuffer.RemoveRange(0, potentialLength);
                                }
                                else
                                {
                                    // CRC 无效，移除第一个字节继续尝试
                                    dataBuffer.RemoveAt(0);
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
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
                _logger.Error($"串口读取错误: {ex.Message}", ex);
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
    /// 解析 Modbus 响应数据
    /// </summary>
    private static void ParseModbusResponse(byte[] frame)
    {
        try
        {
            var slaveId = frame[0];
            var functionCode = frame[1];

            if (functionCode >= 0x80)
            {
                var errorCode = frame[2];
                _logger.Error($"Modbus 错误响应: 从站={slaveId}, 功能码={functionCode:X2}H, 错误码={errorCode:X2}H");
                return;
            }

            var byteCount = frame[2];
            var data = new byte[byteCount];
            Array.Copy(frame, 3, data, 0, byteCount);

            _logger.Info($"Modbus 响应解析: 从站={slaveId}, 功能码={functionCode:X2}H, 数据长度={byteCount}字节");
            
            // 解析输入寄存器数据（功能码 04）
            if (functionCode == 0x04)
            {
                ParseInputRegisters(data);
            }
            // 解析保持寄存器数据（功能码 03）
            else if (functionCode == 0x03)
            {
                ParseHoldingRegisters(data);
            }
            // 解析线圈状态（功能码 01）
            else if (functionCode == 0x01)
            {
                ParseCoils(data);
            }
            // 解析离散输入状态（功能码 02）
            else if (functionCode == 0x02)
            {
                ParseDiscreteInputs(data);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"解析 Modbus 响应失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 解析输入寄存器数据（温湿度值）
    /// </summary>
    private static void ParseInputRegisters(byte[] data)
    {
        if (data.Length < 4) return;

        // 温度（地址0）
        var tempRaw = (ushort)((data[0] << 8) | data[1]);
        var temperature = (short)tempRaw * 0.1;

        // 湿度（地址1）
        var humRaw = (ushort)((data[2] << 8) | data[3]);
        var humidity = humRaw * 0.1;

        _logger.Info("========================================");
        _logger.Info($"温度: {temperature:F1} ℃");
        _logger.Info($"湿度: {humidity:F1} %RH");
        
        if (data.Length >= 6)
        {
            // 露点温度（地址2）
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

        var tempCal = (short)((data[0] << 8) | data[1]) * 0.1;
        var humCal = (short)((data[2] << 8) | data[3]) * 0.1;

        _logger.Info($"温度校准值: {tempCal:F1} ℃");
        _logger.Info($"湿度校准值: {humCal:F1} %RH");
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
    private static async Task PollingSensorDataAsync(ModbusConfig config, SerialPortTestConfig serialConfig)
    {
        _logger.Info("\n开始轮询传感器数据...");
        _logger.Info("按 Ctrl+C 停止程序");

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                // 1. 读取输入寄存器（温湿度）- 功能码 0x04
                await SendModbusCommandAsync(0x04, 0, 3, config.SlaveId);
                await Task.Delay(1000);

                // 2. 读取保持寄存器（校准值）- 功能码 0x03
                await SendModbusCommandAsync(0x03, 0, 2, config.SlaveId);
                await Task.Delay(1000);

                // 3. 读取线圈（设备状态）- 功能码 0x01
                await SendModbusCommandAsync(0x01, 0, 1, config.SlaveId);
                await Task.Delay(1000);

                // 4. 读取离散输入（报警状态）- 功能码 0x02
                await SendModbusCommandAsync(0x02, 0, 1, config.SlaveId);
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
    /// 发送 Modbus RTU 命令
    /// </summary>
    private static async Task SendModbusCommandAsync(byte functionCode, ushort startAddress, ushort quantity, byte slaveId)
    {
        if (_serialPort == null || !_serialPort.IsOpen)
        {
            _logger.Warning("串口未打开，无法发送命令");
            return;
        }

        try
        {
            // 构建 Modbus RTU 请求帧
            var frame = new List<byte>
            {
                slaveId,                          // 从站地址
                functionCode,                     // 功能码
                (byte)(startAddress >> 8),        // 起始地址高字节
                (byte)(startAddress & 0xFF),      // 起始地址低字节
                (byte)(quantity >> 8),            // 数量高字节
                (byte)(quantity & 0xFF)           // 数量低字节
            };

            // 计算 CRC
            var crc = CrcCalculator.Crc16Modbus(frame.ToArray());
            frame.Add((byte)(crc & 0xFF));        // CRC 低字节
            frame.Add((byte)(crc >> 8));          // CRC 高字节

            var commandBytes = frame.ToArray();
            _logger.Debug($"发送命令: {BitConverter.ToString(commandBytes).Replace('-', ' ')}");

            await _serialPort.BaseStream.WriteAsync(commandBytes, 0, commandBytes.Length);
            
            var functionName = GetFunctionName(functionCode);
            _logger.Info($"已发送 {functionName} 命令 (起始地址: {startAddress}, 数量: {quantity})");
        }
        catch (Exception ex)
        {
            _logger.Error($"发送 Modbus 命令失败: {ex.Message}", ex);
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
/// 串口测试配置
/// </summary>
public class SerialPortTestConfig
{
    public string PortName { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9600;
    public int DataBits { get; set; } = 8;
    public StopBits StopBits { get; set; } = StopBits.One;
    public Parity Parity { get; set; } = Parity.None;
    public int ReadTimeout { get; set; } = 1000;
    public int WriteTimeout { get; set; } = 1000;
}
