using System.Net;
using System.Net.Sockets;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.DeviceMock.Protocols.Modbus;
using Vktun.IoT.Connector.DeviceMock.Services;
using Xunit;

namespace Vktun.IoT.Connector.ProtocolTests;

public class DeviceMockModbusTcpServerTests
{
    [Fact]
    public async Task WriteSingleCoil_ShouldReturnStandardFiveBytePdu()
    {
        var port = GetFreeTcpPort();
        var dataStore = new ModbusDataStore();
        dataStore.Initialize(8, 8, 8, 8);
        var server = new ModbusTcpServer("mock-device", 1, port, dataStore, new TestLogger());

        await ((IDeviceSimulator)server).StartAsync(CancellationToken.None);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            using var stream = client.GetStream();

            await stream.WriteAsync(new byte[]
            {
                0x00, 0x01, 0x00, 0x00, 0x00, 0x06,
                0x01, 0x05, 0x00, 0x00, 0xFF, 0x00
            });

            var header = await ReadExactlyAsync(stream, 6);
            var length = (header[4] << 8) | header[5];
            var pdu = await ReadExactlyAsync(stream, length);

            Assert.Equal(6, length);
            Assert.Equal(new byte[] { 0x01, 0x05, 0x00, 0x00, 0xFF, 0x00 }, pdu);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int length)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset));
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }

        return buffer;
    }

    private sealed class TestLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public void Fatal(string message, Exception? exception = null) { }
        public void Log(LogLevel level, string message, Exception? exception = null) { }
    }
}
