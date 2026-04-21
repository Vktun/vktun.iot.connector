using Microsoft.Extensions.DependencyInjection;
using Vktun.IoT.Connector.Configuration.Logging;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Demo;

internal static class Program
{
    private static readonly ILogger Logger = new ConsoleLogger();

    private static async Task Main()
    {
        var services = new ServiceCollection();
        services.AddVktunIoTConnector(options =>
        {
            options.MinimumLogLevel = LogLevel.Info;
        });

        await using var provider = services.BuildServiceProvider();
        var collector = provider.GetRequiredService<IIoTDataCollector>();

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
}
