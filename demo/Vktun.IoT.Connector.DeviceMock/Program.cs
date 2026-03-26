using System.Text.Json;
using Vktun.IoT.Connector.Configuration.Logging;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.DeviceMock.Models;
using Vktun.IoT.Connector.DeviceMock.Protocols.Modbus;
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
            
            var cts = new CancellationTokenSource();
            
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("\n正在停止服务...");
            };
            
            Console.WriteLine("正在启动设备模拟器...");
            await deviceManager.StartAllAsync(cts.Token);
            
            Console.WriteLine("设备模拟器已启动，按 Ctrl+C 退出");
            Console.WriteLine();
            Console.WriteLine("已启动的服务:");
            Console.WriteLine("  - Modbus TCP Server: 端口 502");
            Console.WriteLine();
            
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"程序运行失败: {ex.Message}", ex);
        }
        finally
        {
            await deviceManager.StopAllAsync();
            Console.WriteLine("设备模拟器已停止");
        }
    }
}
