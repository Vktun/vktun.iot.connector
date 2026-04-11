using Vktun.IoT.Connector.Business.Managers;
using Vktun.IoT.Connector.Business.Providers;
using Vktun.IoT.Connector.Business.Services;
using Vktun.IoT.Connector.Business.Factories;
using Vktun.IoT.Connector.Configuration.Logging;
using Vktun.IoT.Connector.Configuration.Providers;
using Vktun.IoT.Connector.Concurrency.Monitors;
using Vktun.IoT.Connector.Concurrency.Schedulers;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Protocol.Factories;

namespace Vktun.IoT.Connector.Demo;

internal static class Program
{
    private static readonly ILogger Logger = new ConsoleLogger();

    private static async Task Main()
    {
        var collector = BuildCollector();
        collector.DeviceStatusChanged += (_, args) =>
            Logger.Info($"Device {args.DeviceId} status changed: {args.OldStatus} -> {args.NewStatus}");
        collector.DeviceError += (_, args) =>
            Logger.Error($"Device {args.DeviceId} error: {args.ErrorMessage}", args.Exception);

        await collector.InitializeAsync().ConfigureAwait(false);
        await collector.StartAsync().ConfigureAwait(false);

        var modbusTcpDevice = new DeviceInfo
        {
            DeviceId = "MODBUS_TCP_DEMO",
            DeviceName = "Modbus TCP Demo Device",
            CommunicationType = CommunicationType.Tcp,
            ConnectionMode = ConnectionMode.Client,
            IpAddress = "127.0.0.1",
            Port = 502,
            ProtocolType = ProtocolType.ModbusTcp,
            ProtocolId = "PLC_TemperatureHumidity_001",
            ProtocolConfigPath = Path.Combine(AppContext.BaseDirectory, "Protocols", "PLC温湿度传感器协议.json")
        };

        await collector.AddDeviceAsync(modbusTcpDevice).ConfigureAwait(false);
        if (await collector.ConnectDeviceAsync(modbusTcpDevice.DeviceId).ConfigureAwait(false))
        {
            var data = await collector.CollectDataAsync(modbusTcpDevice.DeviceId).ConfigureAwait(false);
            if (data != null)
            {
                Logger.Info($"Collected {data.DataItems.Count} points from {data.DeviceId}");
            }
        }

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();

        await collector.StopAsync().ConfigureAwait(false);
        await collector.DisposeAsync().ConfigureAwait(false);
    }

    private static IIoTDataCollector BuildCollector()
    {
        var configProvider = new JsonConfigurationProvider(Logger);
        var sessionManager = new SessionManager(Logger);
        var parserFactory = new ProtocolParserFactory(Logger);
        var channelFactory = new CommunicationChannelFactory(configProvider, Logger);
        var commandExecutor = new DeviceCommandExecutor(channelFactory, parserFactory, configProvider, Logger);
        var deviceManager = new DeviceManager(sessionManager, commandExecutor, Logger);
        var taskScheduler = new Vktun.IoT.Connector.Concurrency.Schedulers.TaskScheduler(configProvider, deviceManager, commandExecutor, Logger);
        var resourceMonitor = new ResourceMonitor(configProvider, Logger);
        var heartbeatManager = new HeartbeatManager(configProvider, Logger);
        var dataProvider = new DataProvider(new DataCache(10_000), Logger);

        return new IoTDataCollector(
            deviceManager,
            sessionManager,
            taskScheduler,
            resourceMonitor,
            configProvider,
            dataProvider,
            heartbeatManager,
            Logger);
    }
}
