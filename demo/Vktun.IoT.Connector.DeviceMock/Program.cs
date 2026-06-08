using System.Text.Json;
using System.Text;
using Vktun.IoT.Connector.Configuration.Logging;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.DeviceMock.Models;
using Vktun.IoT.Connector.DeviceMock.Protocols.Modbus;
using Vktun.IoT.Connector.DeviceMock.Protocols.Siemens;
using Vktun.IoT.Connector.DeviceMock.Services;

namespace Vktun.IoT.Connector.DeviceMock;

class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Any(arg => arg is "help" or "--help" or "-h"))
        {
            PrintUsage();
            return;
        }

        var modbusPort = GetIntOption(args, "--modbus-port", 502);
        var s7Port = GetIntOption(args, "--s7-port", 102);

        Console.WriteLine("====================================");
        Console.WriteLine("  Vktun IoT Connector Device Mock  ");
        Console.WriteLine("====================================");
        Console.WriteLine();

        var logger = new ConsoleLogger();
        var deviceManager = new DeviceManager(logger);

        try
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "device_config.json");
            await deviceManager.LoadConfigAsync(configPath);

            var modbusDataStore = new ModbusDataStore();
            modbusDataStore.Initialize(10000, 10000, 10000, 10000);

            var modbusServer = new ModbusTcpServer("MODBUS_TCP_001", 1, modbusPort, modbusDataStore, logger);
            deviceManager.RegisterSimulator(modbusServer);

            var s7DataManager = new S7DataBlockManager();
            s7DataManager.Initialize(100, 65536, 1024, 1024, 1024);

            var s7Server = new S7Server("S7_1200_001", s7Port, s7DataManager, logger);
            deviceManager.RegisterSimulator(s7Server);

            var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("\nStopping device mock services...");
            };

            Console.WriteLine("Starting device mock services...");
            await deviceManager.StartAllAsync(cts.Token);

            Console.WriteLine("Device mock services are running. Press Ctrl+C to exit.");
            Console.WriteLine();
            Console.WriteLine("Active services:");
            Console.WriteLine($"  - Modbus TCP Server: port {modbusPort}");
            Console.WriteLine($"  - S7 Server: port {s7Port}");
            Console.WriteLine();

            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Program failed: {ex.Message}", ex);
        }
        finally
        {
            await deviceManager.StopAllAsync();
            Console.WriteLine("Device mock services stopped.");
        }
    }

    private static int GetIntOption(string[] args, string optionName, int defaultValue)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg.StartsWith(optionName + "=", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(arg[(optionName.Length + 1)..], out var inlineValue))
            {
                return inlineValue;
            }

            if (string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase) &&
                index + 1 < args.Length &&
                int.TryParse(args[index + 1], out var nextValue))
            {
                return nextValue;
            }
        }

        return defaultValue;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project demo/Vktun.IoT.Connector.DeviceMock");
        Console.WriteLine("  dotnet run --project demo/Vktun.IoT.Connector.DeviceMock -- --modbus-port 1502 --s7-port 1102");
    }
}
