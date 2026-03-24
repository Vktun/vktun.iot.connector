using System.Net.Sockets;
using System.Text;
using Vktun.IoT.Connector.Configuration.Logging;
using Vktun.IoT.Connector.Configuration.Providers;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;

namespace Vktun.IoT.Connector.Demo;

/// <summary>
/// 西门子 S7-1200 PLC 通信测试 Demo
/// 演示如何通过 S7 协议与西门子 S7-1200 PLC 通信
/// </summary>
public class SeminsS71200Test
{
    private static readonly ILogger _logger = new ConsoleLogger();
    private static TcpClient? _tcpClient;
    private static NetworkStream? _networkStream;
    private static readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// 西门子 S7 数据区域类型
    /// </summary>
    private enum S7Area : byte
    {
        I = 0x81,    // 输入区
        Q = 0x82,    // 输出区
        M = 0x83,    // 标志位区
        DB = 0x84    // DB块区
    }

    /// <summary>
    /// 西门子 S7 数据类型大小
    /// </summary>
    private enum S7DataSize : byte
    {
        Bit = 0x01,
        Byte = 0x02,
        Word = 0x04,
        DWord = 0x06,
        Real = 0x08
    }

    /// <summary>
    /// 运行 S7-1200 PLC 测试
    /// </summary>
    public static async Task RunAsync(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("    西门子 S7-1200 PLC 测试程序");
        Console.WriteLine("========================================");
        Console.WriteLine();

        try
        {
            var protocolConfig = await LoadProtocolTemplateAsync();
            if (protocolConfig == null)
            {
                _logger.Error("加载协议模板失败，退出程序");
                return;
            }

            var plcConfig = GetPlcConfigFromArgs(args);

            if (!await ConnectToPlcAsync(plcConfig))
            {
                return;
            }

            if (!await InitializeS7ConnectionAsync(plcConfig))
            {
                return;
            }

            _ = Task.Run(() => ReceiveDataLoop(_cts.Token));

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
    /// 加载 S7-1200 PLC 协议模板
    /// </summary>
    private static async Task<ProtocolConfig?> LoadProtocolTemplateAsync()
    {
        try
        {
            var configProvider = new JsonConfigurationProvider(_logger);
            var templatePath = Path.Combine(AppContext.BaseDirectory, "Protocols", "S7_1200_PLC协议.json");
            
            if (!File.Exists(templatePath))
            {
                templatePath = Path.Combine("Protocols", "S7_1200_PLC协议.json");
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

            _logger.Info($"成功加载协议模板: {protocolConfig.ProtocolName}");
            _logger.Info($"支持的测点数量: {protocolConfig.Points.Count}");
            
            return protocolConfig;
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
    private static S7PlcConfig GetPlcConfigFromArgs(string[] args)
    {
        var config = new S7PlcConfig
        {
            IpAddress = "192.168.0.1",
            Rack = 0,
            Slot = 1,
            Port = 102,
            Timeout = 3000
        };

        if (args.Length > 0)
        {
            config.IpAddress = args[0];
        }

        if (args.Length > 1 && int.TryParse(args[1], out var rack))
        {
            config.Rack = rack;
        }

        if (args.Length > 2 && int.TryParse(args[2], out var slot))
        {
            config.Slot = slot;
        }

        _logger.Info($"PLC 配置: IP={config.IpAddress}, Rack={config.Rack}, Slot={config.Slot}, Port={config.Port}");
        
        return config;
    }

    /// <summary>
    /// 连接到 PLC (TCP连接)
    /// </summary>
    private static async Task<bool> ConnectToPlcAsync(S7PlcConfig config)
    {
        try
        {
            _logger.Info($"正在连接到 PLC: {config.IpAddress}:{config.Port}...");
            
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(config.IpAddress, config.Port);
            _networkStream = _tcpClient.GetStream();
            _networkStream.ReadTimeout = config.Timeout;
            _networkStream.WriteTimeout = config.Timeout;

            _logger.Info("TCP 连接成功");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"PLC 连接失败: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// 初始化 S7 协议连接 (ISO 连接 + S7 握手)
    /// </summary>
    private static async Task<bool> InitializeS7ConnectionAsync(S7PlcConfig config)
    {
        try
        {
            _logger.Info("正在初始化 S7 协议连接...");

            if (!await SendIsoConnectionRequestAsync(config))
            {
                _logger.Error("ISO 连接请求失败");
                return false;
            }

            var isoResponse = await ReceiveIsoResponseAsync();
            if (isoResponse == null || !VerifyIsoConnectionResponse(isoResponse))
            {
                _logger.Error("ISO 连接响应验证失败");
                return false;
            }

            _logger.Info("ISO 连接成功");

            if (!await SendS7ConnectionRequestAsync())
            {
                _logger.Error("S7 连接请求失败");
                return false;
            }

            var s7Response = await ReceiveS7ResponseAsync();
            if (s7Response == null || !VerifyS7ConnectionResponse(s7Response))
            {
                _logger.Error("S7 连接响应验证失败");
                return false;
            }

            _logger.Info("S7 协议连接成功");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"S7 协议初始化失败: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// 发送 ISO 连接请求 (COTP)
    /// </summary>
    private static async Task<bool> SendIsoConnectionRequestAsync(S7PlcConfig config)
    {
        try
        {
            var pduType = (config.Rack * 0x20) + config.Slot;
            
            var isoPacket = new byte[]
            {
                0x03, 0x00, 0x00, 0x16,
                0x11, 0xE0, 0x00, 0x00, 0x00, 0x01, 0x00, 0xC1,
                0x02, 0x01, 0x00, 0xC2, 0x02, 0x01, (byte)pduType,
                0xC0, 0x01, 0x0A
            };

            if (_networkStream == null) return false;
            await _networkStream.WriteAsync(isoPacket, 0, isoPacket.Length);
            _logger.Debug("已发送 ISO 连接请求");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"发送 ISO 连接请求失败: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// 接收 ISO 响应
    /// </summary>
    private static async Task<byte[]?> ReceiveIsoResponseAsync()
    {
        try
        {
            var buffer = new byte[1024];
            if (_networkStream == null) return null;
            
            var bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead > 0)
            {
                var response = new byte[bytesRead];
                Array.Copy(buffer, response, bytesRead);
                _logger.Debug($"收到 ISO 响应: {BitConverter.ToString(response).Replace('-', ' ')}");
                return response;
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error($"接收 ISO 响应失败: {ex.Message}", ex);
            return null;
        }
    }

    /// <summary>
    /// 验证 ISO 连接响应
    /// </summary>
    private static bool VerifyIsoConnectionResponse(byte[] response)
    {
        if (response.Length < 7) return false;
        return response[5] == 0xD0;
    }

    /// <summary>
    /// 发送 S7 连接请求
    /// </summary>
    private static async Task<bool> SendS7ConnectionRequestAsync()
    {
        try
        {
            var s7Packet = new byte[]
            {
                0x03, 0x00, 0x00, 0x19,
                0x02, 0xF0, 0x80, 0x32, 0x01, 0x00, 0x00, 0x04,
                0x00, 0x00, 0x08, 0x00, 0x00, 0xF0, 0x00, 0x00,
                0x01, 0x00, 0x01, 0x01, 0xE0
            };

            if (_networkStream == null) return false;
            await _networkStream.WriteAsync(s7Packet, 0, s7Packet.Length);
            _logger.Debug("已发送 S7 连接请求");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"发送 S7 连接请求失败: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// 接收 S7 响应
    /// </summary>
    private static async Task<byte[]?> ReceiveS7ResponseAsync()
    {
        try
        {
            var buffer = new byte[1024];
            if (_networkStream == null) return null;
            
            var bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead > 0)
            {
                var response = new byte[bytesRead];
                Array.Copy(buffer, response, bytesRead);
                _logger.Debug($"收到 S7 响应: {BitConverter.ToString(response).Replace('-', ' ')}");
                return response;
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error($"接收 S7 响应失败: {ex.Message}", ex);
            return null;
        }
    }

    /// <summary>
    /// 验证 S7 连接响应
    /// </summary>
    private static bool VerifyS7ConnectionResponse(byte[] response)
    {
        if (response.Length < 11) return false;
        return response[7] == 0x32 && response[10] == 0x00;
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
                        
                        ParseS7Response(receivedData);
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
    /// 解析 S7 响应数据
    /// </summary>
    private static void ParseS7Response(byte[] frame)
    {
        try
        {
            if (frame.Length < 17)
            {
                _logger.Warning("响应帧长度不足，无法解析");
                return;
            }

            if (frame[7] != 0x32)
            {
                _logger.Warning("非 S7 协议响应");
                return;
            }

            var pduType = frame[8];
            var errorCode = (ushort)((frame[10] << 8) | frame[11]);

            if (errorCode != 0)
            {
                _logger.Error($"S7 错误响应: 错误码=0x{errorCode:X4}");
                return;
            }

            if (pduType == 0x03)
            {
                ParseS7ReadResponse(frame);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"解析 S7 响应失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 解析 S7 读取响应
    /// </summary>
    private static void ParseS7ReadResponse(byte[] frame)
    {
        try
        {
            var dataCount = frame[12];
            var dataOffset = 14;

            _logger.Info("========================================");
            _logger.Info("PLC 数据读取结果:");

            for (int i = 0; i < dataCount && dataOffset < frame.Length - 4; i++)
            {
                var returnCode = frame[dataOffset];
                var dataSize = frame[dataOffset + 1];
                
                if (returnCode == 0xFF)
                {
                    var data = new byte[4];
                    if (dataOffset + 4 < frame.Length)
                    {
                        Array.Copy(frame, dataOffset + 4, data, 0, 4);
                        var value = ParseS7Data(data, dataSize);
                        _logger.Info($"数据项 {i + 1}: {value}");
                    }
                }
                else
                {
                    _logger.Warning($"数据项 {i + 1}: 读取失败，返回码=0x{returnCode:X2}");
                }

                dataOffset += 4 + GetDataLengthBySize(dataSize);
            }
            _logger.Info("========================================");
        }
        catch (Exception ex)
        {
            _logger.Error($"解析 S7 读取响应失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 根据数据大小获取数据长度
    /// </summary>
    private static int GetDataLengthBySize(byte dataSize)
    {
        return dataSize switch
        {
            0x01 => 1,
            0x02 => 1,
            0x04 => 2,
            0x06 => 4,
            0x08 => 4,
            _ => 4
        };
    }

    /// <summary>
    /// 解析 S7 数据
    /// </summary>
    private static object ParseS7Data(byte[] data, byte dataSize)
    {
        try
        {
            return dataSize switch
            {
                0x01 => (data[0] & 0x01) != 0,
                0x02 => data[0],
                0x04 => (ushort)((data[0] << 8) | data[1]),
                0x06 => (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]),
                0x08 => ParseS7Real(data),
                _ => BitConverter.ToString(data)
            };
        }
        catch
        {
            return BitConverter.ToString(data);
        }
    }

    /// <summary>
    /// 解析 S7 Real (IEEE 754 单精度浮点数)
    /// </summary>
    private static float ParseS7Real(byte[] data)
    {
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(data);
        }
        return BitConverter.ToSingle(data, 0);
    }

    /// <summary>
    /// 轮询传感器数据
    /// </summary>
    private static async Task PollingSensorDataAsync(ProtocolConfig config, S7PlcConfig plcConfig)
    {
        _logger.Info("\n开始轮询 PLC 数据...");
        _logger.Info("按 Ctrl+C 停止程序");

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await ReadRealValueAsync(1, 0);
                await Task.Delay(500);

                await ReadRealValueAsync(1, 4);
                await Task.Delay(500);

                await ReadBoolValueAsync(S7Area.M, 0, 0, 0);
                await Task.Delay(500);

                await ReadBoolValueAsync(S7Area.M, 0, 0, 1);
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
    /// 读取 Real 类型数据
    /// </summary>
    private static async Task ReadRealValueAsync(int dbNumber, int startAddress)
    {
        try
        {
            var request = BuildS7ReadRequest(S7Area.DB, dbNumber, startAddress, S7DataSize.Real, 1);
            if (request == null || _networkStream == null) return;

            await _networkStream.WriteAsync(request, 0, request.Length);
            _logger.Info($"已发送读取 DB{dbNumber}.DBX{startAddress} (Real) 请求");
        }
        catch (Exception ex)
        {
            _logger.Error($"读取 Real 数据失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 读取 Bool 类型数据
    /// </summary>
    private static async Task ReadBoolValueAsync(S7Area area, int dbNumber, int byteAddress, int bitPosition)
    {
        try
        {
            var request = BuildS7ReadRequest(area, dbNumber, byteAddress * 8 + bitPosition, S7DataSize.Bit, 1);
            if (request == null || _networkStream == null) return;

            await _networkStream.WriteAsync(request, 0, request.Length);
            _logger.Info($"已发送读取 {area}{dbNumber}.DBX{byteAddress}.{bitPosition} (Bool) 请求");
        }
        catch (Exception ex)
        {
            _logger.Error($"读取 Bool 数据失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 构建 S7 读取请求
    /// </summary>
    private static byte[]? BuildS7ReadRequest(S7Area area, int dbNumber, int startAddress, S7DataSize dataSize, int count)
    {
        try
        {
            var header = new byte[]
            {
                0x03, 0x00,
                0x00, 0x00,
                0x02, 0xF0, 0x80,
                0x32, 0x01, 0x00, 0x00, 0x04,
                0x00, 0x00, 0x0E, 0x00, 0x00, 0x04, 0x01, 0x12,
                0x08, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00,
                (byte)area,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00
            };

            var addressBytes = BitConverter.GetBytes(startAddress);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(addressBytes);
            }

            header[31] = (byte)(count >> 8);
            header[32] = (byte)(count & 0xFF);
            header[33] = (byte)((int)dataSize);
            header[35] = (byte)(dbNumber >> 8);
            header[36] = (byte)(dbNumber & 0xFF);
            header[37] = addressBytes[1];
            header[38] = addressBytes[2];
            header[39] = addressBytes[3];

            var packetLength = header.Length;
            header[2] = (byte)(packetLength >> 8);
            header[3] = (byte)(packetLength & 0xFF);

            return header;
        }
        catch (Exception ex)
        {
            _logger.Error($"构建 S7 读取请求失败: {ex.Message}", ex);
            return null;
        }
    }

    /// <summary>
    /// 获取区域名称
    /// </summary>
    private static string GetAreaName(S7Area area)
    {
        return area switch
        {
            S7Area.I => "输入区(I)",
            S7Area.Q => "输出区(Q)",
            S7Area.M => "标志位区(M)",
            S7Area.DB => "数据块(DB)",
            _ => "未知区域"
        };
    }
}

/// <summary>
/// S7 PLC 配置
/// </summary>
public class S7PlcConfig
{
    public string IpAddress { get; set; } = "192.168.0.1";
    public int Rack { get; set; } = 0;
    public int Slot { get; set; } = 1;
    public int Port { get; set; } = 102;
    public int Timeout { get; set; } = 3000;
}