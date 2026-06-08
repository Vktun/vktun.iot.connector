using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Vktun.IoT.Connector.Configuration.Logging;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Demo;

internal static class Program
{
    private static readonly ILogger Logger = new ConsoleLogger();

    private static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var collectorArgs = Array.Empty<string>();
        if (args.Length > 0)
        {
            var demoArgs = args.Skip(1).ToArray();
            switch (args[0].ToLowerInvariant())
            {
                case "default":
                case "collector":
                    collectorArgs = demoArgs;
                    break;
                case "modbus-tcp":
                    await ModbusTcpTest.RunAsync(demoArgs).ConfigureAwait(false);
                    return;
                case "s7":
                    await SeminsS71200Test.RunAsync(demoArgs).ConfigureAwait(false);
                    return;
                case "serial":
                    await SerialPortTest.RunAsync(demoArgs).ConfigureAwait(false);
                    return;
                case "help":
                case "--help":
                case "-h":
                    PrintUsage();
                    return;
                default:
                    Console.WriteLine($"Unknown demo '{args[0]}'.");
                    PrintUsage();
                    return;
            }
        }

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
            IpAddress = GetStringArg(collectorArgs, 0, "127.0.0.1"),
            Port = GetIntArg(collectorArgs, 1, 502),
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

        WaitForExit("Press any key to exit...");

        await collector.StopAsync().ConfigureAwait(false);
        await collector.DisposeAsync().ConfigureAwait(false);
    }

    internal static void WaitForExit(string prompt)
    {
        Console.WriteLine(prompt);
        if (Console.IsInputRedirected)
        {
            return;
        }

        Console.ReadKey(intercept: true);
        Console.WriteLine();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project demo/Vktun.IoT.Connector.Demo -- [collector|default] [ip] [port]");
        Console.WriteLine("  dotnet run --project demo/Vktun.IoT.Connector.Demo -- modbus-tcp [ip] [port] [slaveId]");
        Console.WriteLine("  dotnet run --project demo/Vktun.IoT.Connector.Demo -- s7 [ip] [rack] [slot]");
        Console.WriteLine("  dotnet run --project demo/Vktun.IoT.Connector.Demo -- serial [portName] [baudRate]");
    }

    private static string GetStringArg(string[] args, int index, string defaultValue)
    {
        return index < args.Length && !string.IsNullOrWhiteSpace(args[index])
            ? args[index]
            : defaultValue;
    }

    private static int GetIntArg(string[] args, int index, int defaultValue)
    {
        return index < args.Length && int.TryParse(args[index], out var value)
            ? value
            : defaultValue;
    }
}
