using Vktun.IoT.Connector.Api;
using Vktun.IoT.Connector.Business.Managers;
using Vktun.IoT.Connector.Business.Providers;
using Vktun.IoT.Connector.Configuration.Logging;
using Vktun.IoT.Connector.Configuration.Providers;
using Vktun.IoT.Connector.Concurrency.Monitors;
using Vktun.IoT.Connector.Concurrency.Schedulers;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Protocol.Factories;

namespace Vktun.IoT.Connector.Demo;

class Program
{
    private static IIoTDataCollector? _collector;
    private static readonly ILogger _logger = new ConsoleLogger();
    private static readonly CancellationTokenSource _cts = new();

    static async Task Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("  Vktun IoT Connector Demo - 上位机采集程序");
        Console.WriteLine("========================================");
        Console.WriteLine();

        try
        {
            await RunDemoAsync();
        }
        catch (Exception ex)
        {
            _logger.Fatal($"程序运行异常: {ex.Message}", ex);
        }
        finally
        {
            if (_collector != null)
            {
                await _collector.StopAsync();
                await _collector.DisposeAsync();
            }
        }

        Console.WriteLine("\n按任意键退出...");
        Console.ReadKey();
    }

    static async Task RunDemoAsync()
    {
        _logger.Info("正在初始化 IoT Connector...");

        var configProvider = new JsonConfigurationProvider(_logger);
        var sessionManager = new SessionManager(_logger);
        var dataCache = new DataCache(10000);
        var dataProvider = new DataProvider(dataCache, _logger);
        var deviceManager = new DeviceManager(sessionManager, configProvider, _logger);
        var taskScheduler = new Vktun.IoT.Connector.Concurrency.Schedulers.TaskScheduler(configProvider, _logger);
        var resourceMonitor = new ResourceMonitor(configProvider, _logger);
        var heartbeatManager = new HeartbeatManager(configProvider, _logger);

        _collector = new IoTDataCollector(
            deviceManager,
            sessionManager,
            taskScheduler,
            resourceMonitor,
            configProvider,
            dataProvider,
            heartbeatManager,
            _logger);

        _collector.DeviceStatusChanged += OnDeviceStatusChanged;
        _collector.DeviceError += OnDeviceError;
        _collector.ResourceThresholdExceeded += OnResourceThresholdExceeded;

        var config = CreateDemoConfig();
        await _collector.InitializeAsync(config);

        _logger.Info("正在启动 IoT Connector...");
        await _collector.StartAsync(_cts.Token);

        await DemoTcpCommunication();
        await DemoUdpCommunication();
        await DemoSerialCommunication();
        await DemoProtocolParsing();
        await DemoProtocolTemplateLoading();

        _logger.Info("演示完成，运行5秒后自动停止...");
        await Task.Delay(5000);
    }

    static SdkConfig CreateDemoConfig()
    {
        return new SdkConfig
        {
            Global = new GlobalConfig
            {
                MaxConcurrentConnections = 100,
                BufferSize = 8192,
                ConnectionTimeout = 5000,
                MaxReconnectCount = 10,
                ReconnectBaseInterval = 1000,
                ReconnectMaxInterval = 30000,
                EnableDataCache = true,
                CacheMaxSize = 1000
            },
            Tcp = new TcpConfig
            {
                MaxServerConnections = 50,
                HeartbeatInterval = 15000,
                HeartbeatTimeout = 30000,
                NoDelay = true,
                SessionIdleTimeout = 3600000,
                ListenBacklog = 100,
                ReceiveBufferSize = 8192,
                SendBufferSize = 8192
            },
            Udp = new UdpConfig
            {
                MaxOnlineDevices = 100,
                HeartbeatCheckInterval = 20000,
                DeviceOfflineTimeout = 40000,
                ReceiveBufferSize = 65536,
                MaxDataRate = 1000
            },
            Serial = new SerialConfig
            {
                MaxDevicesPerPort = 32,
                PollingInterval = 100,
                ReceivePollingInterval = 10,
                ReadWriteTimeout = 500,
                MaxConcurrentPorts = 4
            },
            ThreadPool = new ThreadPoolConfig
            {
                MinWorkerThreads = 10,
                MaxWorkerThreads = 100,
                MinCompletionPortThreads = 10,
                MaxCompletionPortThreads = 100,
                TaskQueueCapacity = 10000
            },
            Resource = new ResourceConfig
            {
                MaxCpuUsage = 80,
                MaxMemoryUsage = 1024 * 1024 * 1024,
                MaxSocketHandles = 10000,
                MonitorInterval = 5000,
                EnableResourceMonitor = true
            }
        };
    }

    static async Task DemoTcpCommunication()
    {
        _logger.Info("\n--- TCP 通信演示 ---");

        var tcpDevice = new DeviceInfo
        {
            DeviceId = "TCP_DEVICE_001",
            DeviceName = "TCP测试设备",
            CommunicationType = CommunicationType.Tcp,
            ConnectionMode = ConnectionMode.Client,
            IpAddress = "127.0.0.1",
            Port = 8888,
            ProtocolType = ProtocolType.Custom,
            ProtocolId = "CustomProtocol"
        };

        var added = await _collector!.AddDeviceAsync(tcpDevice);
        _logger.Info($"添加TCP设备: {(added ? "成功" : "失败")}");

        _logger.Info("尝试连接TCP设备...");
        var connected = await _collector.ConnectDeviceAsync(tcpDevice.DeviceId);
        _logger.Info($"TCP设备连接: {(connected ? "成功" : "失败 - 请确保目标服务器正在运行")}");

        if (connected)
        {
            var command = new DeviceCommand
            {
                DeviceId = tcpDevice.DeviceId,
                CommandName = "ReadData",
                Priority = TaskPriority.High,
                Timeout = 5000
            };

            var result = await _collector.SendCommandAsync(command);
            _logger.Info($"命令执行结果: {(result.Success ? "成功" : $"失败 - {result.ErrorMessage}")}");
        }
    }

    static async Task DemoUdpCommunication()
    {
        _logger.Info("\n--- UDP 通信演示 ---");

        var udpDevice = new DeviceInfo
        {
            DeviceId = "UDP_DEVICE_001",
            DeviceName = "UDP测试设备",
            CommunicationType = CommunicationType.Udp,
            ConnectionMode = ConnectionMode.Client,
            IpAddress = "127.0.0.1",
            Port = 9999,
            ProtocolType = ProtocolType.Custom,
            ProtocolId = "CustomProtocol"
        };

        var added = await _collector!.AddDeviceAsync(udpDevice);
        _logger.Info($"添加UDP设备: {(added ? "成功" : "失败")}");

        _logger.Info("尝试连接UDP设备...");
        var connected = await _collector.ConnectDeviceAsync(udpDevice.DeviceId);
        _logger.Info($"UDP设备连接: {(connected ? "成功" : "失败")}");

        if (connected)
        {
            var data = await _collector.CollectDataAsync(udpDevice.DeviceId);
            if (data != null)
            {
                _logger.Info($"采集数据成功: 设备={data.DeviceId}, 时间={data.CollectTime}");
            }
        }
    }

    static async Task DemoSerialCommunication()
    {
        _logger.Info("\n--- 串口通信演示 ---");

        var serialDevice = new DeviceInfo
        {
            DeviceId = "SERIAL_DEVICE_001",
            DeviceName = "串口测试设备",
            CommunicationType = CommunicationType.Serial,
            ConnectionMode = ConnectionMode.Server,
            SerialPort = "COM1",
            BaudRate = 9600,
            ProtocolType = ProtocolType.ModbusRtu,
            ProtocolId = "ModbusRtu"
        };

        var added = await _collector!.AddDeviceAsync(serialDevice);
        _logger.Info($"添加串口设备: {(added ? "成功" : "失败")}");

        _logger.Info("尝试连接串口设备...");
        var connected = await _collector.ConnectDeviceAsync(serialDevice.DeviceId);
        _logger.Info($"串口设备连接: {(connected ? "成功" : "失败 - 请确保串口可用")}");
    }

    static async Task DemoProtocolParsing()
    {
        _logger.Info("\n--- 协议解析演示 ---");

        var parserFactory = new ProtocolParserFactory(_logger);
        var parser = parserFactory.GetParser(ProtocolType.Custom);

        if (parser != null)
        {
            var protocolConfig = new CustomProtocolConfig
            {
                ProtocolId = "CustomProtocol",
                ProtocolName = "自定义协议",
                FrameType = FrameType.VariableLength,
                ByteOrder = ByteOrder.BigEndian,
                FrameHeader = new FrameHeaderConfig
                {
                    Value = new byte[] { 0xAA, 0x55 },
                    Length = 2
                },
                FrameLength = new FrameLengthConfig
                {
                    Offset = 2,
                    Length = 2,
                    CalcRule = "Self"
                },
                Points = new List<PointConfig>
                {
                    new PointConfig
                    {
                        PointName = "温度",
                        Offset = 4,
                        Length = 2,
                        DataType = DataType.UInt16,
                        Ratio = 0.1,
                        Unit = "℃"
                    },
                    new PointConfig
                    {
                        PointName = "湿度",
                        Offset = 6,
                        Length = 2,
                        DataType = DataType.UInt16,
                        Ratio = 0.1,
                        Unit = "%"
                    }
                }
            };

            byte[] sampleData = new byte[] { 0xAA, 0x55, 0x00, 0x08, 0x01, 0xF4, 0x01, 0x90 };

            _logger.Info($"原始数据: {BitConverter.ToString(sampleData)}");

            var isValid = parser.Validate(sampleData, new ProtocolConfig
            {
                ProtocolId = protocolConfig.ProtocolId,
                ProtocolType = ProtocolType.Custom
            });
            _logger.Info($"数据校验: {(isValid ? "通过" : "失败")}");

            var parsedData = parser.Parse(sampleData, new ProtocolConfig
            {
                ProtocolId = protocolConfig.ProtocolId,
                ProtocolType = ProtocolType.Custom,
                Points = protocolConfig.Points
            });

            foreach (var data in parsedData)
            {
                _logger.Info($"解析结果: 设备={data.DeviceId}");
                foreach (var point in data.DataItems)
                {
                    _logger.Info($"  {point.PointName}: {point.Value} {point.Unit}");
                }
            }
        }

        await Task.CompletedTask;
    }

    static async Task DemoProtocolTemplateLoading()
    {
        _logger.Info("\n--- 协议模板加载演示 ---");

        var configProvider = new JsonConfigurationProvider(_logger);

        var templatesPath = Path.Combine(AppContext.BaseDirectory, "Protocols");
        Directory.CreateDirectory(templatesPath);

        var sampleTemplate = new ProtocolConfig
        {
            ProtocolId = "DemoProtocol_001",
            ProtocolName = "演示协议模板",
            Description = "这是一个用于演示的协议模板",
            ProtocolType = ProtocolType.Custom,
            ChannelId = "Channel_001",
            ParseRules = new Dictionary<string, string>
            {
                { "FrameHeader", "0xAA,0x55" },
                { "FrameLength", "2,2" },
                { "CheckType", "Crc16Modbus" }
            },
            Points = new List<PointConfig>
            {
                new PointConfig
                {
                    PointName = "温度",
                    Offset = 4,
                    Length = 2,
                    DataType = DataType.UInt16,
                    Ratio = 0.1,
                    Unit = "℃",
                    Description = "环境温度"
                },
                new PointConfig
                {
                    PointName = "湿度",
                    Offset = 6,
                    Length = 2,
                    DataType = DataType.UInt16,
                    Ratio = 0.1,
                    Unit = "%RH",
                    Description = "环境湿度"
                }
            }
        };

        var templatePath = Path.Combine(templatesPath, "DemoProtocol.json");
        await configProvider.SaveProtocolTemplateAsync(templatePath, sampleTemplate);
        _logger.Info($"协议模板已保存到: {templatePath}");

        var loadedTemplate = await configProvider.LoadProtocolTemplateAsync(templatePath);
        if (loadedTemplate != null)
        {
            _logger.Info($"加载协议模板成功: {loadedTemplate.ProtocolName}");
            _logger.Info($"协议ID: {loadedTemplate.ProtocolId}");
            _logger.Info($"协议类型: {loadedTemplate.ProtocolType}");
            _logger.Info($"解析点数量: {loadedTemplate.Points.Count}");
            foreach (var point in loadedTemplate.Points)
            {
                _logger.Info($"  - {point.PointName}: {point.DataType}, 偏移={point.Offset}, 长度={point.Length}");
            }
        }

        var allTemplates = await configProvider.LoadProtocolTemplatesAsync(templatesPath);
        _logger.Info($"目录中共加载了 {allTemplates.Count} 个协议模板");

        var allPaths = await configProvider.GetProtocolTemplatePathsAsync(templatesPath);
        foreach (var path in allPaths)
        {
            _logger.Info($"  - {Path.GetFileName(path)}");
        }
    }

    static void OnDeviceStatusChanged(object? sender, DeviceStatusChangedEventArgs e)
    {
        _logger.Info($"设备状态变更: {e.DeviceId}, {e.OldStatus} -> {e.NewStatus}");
    }

    static void OnDeviceError(object? sender, DeviceErrorEventArgs e)
    {
        _logger.Error($"设备错误: {e.DeviceId}, {e.ErrorMessage}", e.Exception);
    }

    static void OnResourceThresholdExceeded(object? sender, ResourceThresholdExceededEventArgs e)
    {
        _logger.Warning($"资源阈值超限: {e.ResourceType}, 当前值={e.CurrentValue}, 阈值={e.ThresholdValue}");
    }
}

internal class ConsoleLogger : ILogger
{
    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var levelStr = level.ToString().ToUpper().PadLeft(7);
        var color = level switch
        {
            LogLevel.Debug => ConsoleColor.Gray,
            LogLevel.Info => ConsoleColor.White,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Fatal => ConsoleColor.DarkRed,
            _ => ConsoleColor.White
        };

        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine($"[{timestamp}] [{levelStr}] {message}");
        if (exception != null)
        {
            Console.WriteLine($"  异常: {exception.GetType().Name}: {exception.Message}");
        }
        Console.ForegroundColor = originalColor;
    }

    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message, Exception? exception = null) => Log(LogLevel.Error, message, exception);
    public void Fatal(string message, Exception? exception = null) => Log(LogLevel.Fatal, message, exception);
}
