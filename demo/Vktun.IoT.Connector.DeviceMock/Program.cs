using System.Text.Json;
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

            var modbusServer = new ModbusTcpServer("MODBUS_TCP_001", 1, 502, modbusDataStore, logger);
            deviceManager.RegisterSimulator(modbusServer);

            var s7DataManager = new S7DataBlockManager();
            s7DataManager.Initialize(100, 65536, 1024, 1024, 1024);

            var s7Server = new S7Server("S7_1200_001", 102, s7DataManager, logger);
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
            Console.WriteLine("  - Modbus TCP Server: port 502");
            Console.WriteLine("  - S7 Server: port 102");
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
}
