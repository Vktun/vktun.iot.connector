using System.Net;
using System.Net.Sockets;
using Vktun.IoT.Connector.Business.Services;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.Services;

public class ModbusSlaveServerTests
{
    [Theory]
    [MemberData(nameof(CommonFunctionFrames))]
    public async Task TcpServer_CommonFunction_ShouldReturnExactFrame(byte[] request, byte[] expected)
    {
        await using var server = new ModbusSlaveServer(new TestLogger());
        var store = CreateSeededStore();
        var port = GetFreeTcpPort();

        var started = await server.StartAsync(new ModbusSlaveOptions
        {
            ListenAddress = "127.0.0.1",
            Port = port,
            SlaveId = 0x11
        }, store);

        Assert.True(started.Success);

        var response = await SendAndReceiveAsync(port, request);

        Assert.Equal(expected, response);
    }

    [Fact]
    public async Task TcpServer_WriteRequests_ShouldMutateDataStore()
    {
        await using var server = new ModbusSlaveServer(new TestLogger());
        var store = CreateSeededStore();
        var port = GetFreeTcpPort();
        await server.StartAsync(new ModbusSlaveOptions { ListenAddress = "127.0.0.1", Port = port, SlaveId = 0x11 }, store);

        await SendAndReceiveAsync(port, Frame(1, 0x11, 0x05, 0x00, 0x02, 0xFF, 0x00));
        await SendAndReceiveAsync(port, Frame(2, 0x11, 0x06, 0x00, 0x02, 0x12, 0x34));
        await SendAndReceiveAsync(port, Frame(3, 0x11, 0x0F, 0x00, 0x04, 0x00, 0x03, 0x01, 0x05));
        await SendAndReceiveAsync(port, Frame(4, 0x11, 0x10, 0x00, 0x04, 0x00, 0x02, 0x04, 0x00, 0x0A, 0x01, 0x02));

        Assert.True(store.GetCoil(2));
        Assert.Equal(new[] { true, false, true }, store.ReadCoils(4, 3));
        Assert.Equal(0x1234, store.GetHoldingRegister(2));
        Assert.Equal(new ushort[] { 0x000A, 0x0102 }, store.ReadHoldingRegisters(4, 2));
    }

    [Fact]
    public async Task TcpServer_UnsupportedFunction_ShouldReturnIllegalFunctionException()
    {
        await using var server = new ModbusSlaveServer(new TestLogger());
        var port = GetFreeTcpPort();
        await server.StartAsync(new ModbusSlaveOptions { ListenAddress = "127.0.0.1", Port = port, SlaveId = 0x11 }, CreateSeededStore());

        var response = await SendAndReceiveAsync(port, Frame(1, 0x11, 0x2B, 0x0E));

        Assert.Equal(Frame(1, 0x11, 0xAB, 0x01), response);
    }

    [Fact]
    public async Task TcpServer_InvalidQuantity_ShouldReturnIllegalDataValueException()
    {
        await using var server = new ModbusSlaveServer(new TestLogger());
        var port = GetFreeTcpPort();
        await server.StartAsync(new ModbusSlaveOptions { ListenAddress = "127.0.0.1", Port = port, SlaveId = 0x11 }, CreateSeededStore());

        var response = await SendAndReceiveAsync(port, Frame(1, 0x11, 0x03, 0x00, 0x00, 0x00, 0x7E));

        Assert.Equal(Frame(1, 0x11, 0x83, 0x03), response);
    }

    [Fact]
    public async Task TcpServer_WrongUnitId_ShouldNotRespondAndShouldRaiseTraffic()
    {
        await using var server = new ModbusSlaveServer(new TestLogger());
        var traffic = new List<ModbusSlaveTrafficEntry>();
        server.TrafficReceived += (_, entry) => traffic.Add(entry);

        var port = GetFreeTcpPort();
        await server.StartAsync(new ModbusSlaveOptions { ListenAddress = "127.0.0.1", Port = port, SlaveId = 0x11 }, CreateSeededStore());

        var response = await SendAndReceiveOptionalAsync(port, Frame(1, 0x12, 0x03, 0x00, 0x00, 0x00, 0x01), TimeSpan.FromMilliseconds(200));

        Assert.Null(response);
        var entry = Assert.Single(traffic);
        Assert.False(entry.Success);
        Assert.Equal(0x12, entry.UnitId);
        Assert.Empty(entry.ResponseFrame);
    }

    [Fact]
    public async Task TcpServer_InvalidProtocolId_ShouldNotRespondAndShouldRaiseTraffic()
    {
        await using var server = new ModbusSlaveServer(new TestLogger());
        var traffic = new List<ModbusSlaveTrafficEntry>();
        server.TrafficReceived += (_, entry) => traffic.Add(entry);

        var port = GetFreeTcpPort();
        await server.StartAsync(new ModbusSlaveOptions { ListenAddress = "127.0.0.1", Port = port, SlaveId = 0x11 }, CreateSeededStore());

        var request = new byte[] { 0x00, 0x01, 0x00, 0x01, 0x00, 0x06, 0x11, 0x03, 0x00, 0x00, 0x00, 0x01 };
        var response = await SendAndReceiveOptionalAsync(port, request, TimeSpan.FromMilliseconds(200));

        Assert.Null(response);
        var entry = Assert.Single(traffic);
        Assert.False(entry.Success);
        Assert.Contains("protocol", entry.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<object[]> CommonFunctionFrames()
    {
        yield return new object[]
        {
            Frame(1, 0x11, 0x01, 0x00, 0x00, 0x00, 0x0A),
            Frame(1, 0x11, 0x01, 0x02, 0x8D, 0x02)
        };
        yield return new object[]
        {
            Frame(1, 0x11, 0x02, 0x00, 0x00, 0x00, 0x0A),
            Frame(1, 0x11, 0x02, 0x02, 0x9A, 0x01)
        };
        yield return new object[]
        {
            Frame(1, 0x11, 0x03, 0x00, 0x00, 0x00, 0x02),
            Frame(1, 0x11, 0x03, 0x04, 0x12, 0x34, 0x56, 0x78)
        };
        yield return new object[]
        {
            Frame(1, 0x11, 0x04, 0x00, 0x00, 0x00, 0x02),
            Frame(1, 0x11, 0x04, 0x04, 0x00, 0x0A, 0x01, 0x02)
        };
        yield return new object[]
        {
            Frame(1, 0x11, 0x05, 0x00, 0x01, 0xFF, 0x00),
            Frame(1, 0x11, 0x05, 0x00, 0x01, 0xFF, 0x00)
        };
        yield return new object[]
        {
            Frame(1, 0x11, 0x06, 0x00, 0x01, 0x00, 0x03),
            Frame(1, 0x11, 0x06, 0x00, 0x01, 0x00, 0x03)
        };
        yield return new object[]
        {
            Frame(1, 0x11, 0x0F, 0x00, 0x00, 0x00, 0x0A, 0x02, 0x8D, 0x02),
            Frame(1, 0x11, 0x0F, 0x00, 0x00, 0x00, 0x0A)
        };
        yield return new object[]
        {
            Frame(1, 0x11, 0x10, 0x00, 0x00, 0x00, 0x02, 0x04, 0x00, 0x0A, 0x01, 0x02),
            Frame(1, 0x11, 0x10, 0x00, 0x00, 0x00, 0x02)
        };
    }

    private static ModbusSlaveDataStore CreateSeededStore()
    {
        var store = new ModbusSlaveDataStore(coilCount: 32, discreteInputCount: 32, inputRegisterCount: 32, holdingRegisterCount: 32);
        store.WriteCoils(0, new[] { true, false, true, true, false, false, false, true, false, true });
        store.WriteDiscreteInputs(0, new[] { false, true, false, true, true, false, false, true, true, false });
        store.WriteHoldingRegisters(0, new ushort[] { 0x1234, 0x5678 });
        store.WriteInputRegisters(0, new ushort[] { 0x000A, 0x0102 });
        return store;
    }

    private static byte[] Frame(ushort transactionId, byte unitId, byte functionCode, params byte[] pduTail)
    {
        var frame = new byte[8 + pduTail.Length];
        frame[0] = (byte)(transactionId >> 8);
        frame[1] = (byte)(transactionId & 0xFF);
        frame[2] = 0x00;
        frame[3] = 0x00;
        frame[4] = (byte)((pduTail.Length + 2) >> 8);
        frame[5] = (byte)((pduTail.Length + 2) & 0xFF);
        frame[6] = unitId;
        frame[7] = functionCode;
        Array.Copy(pduTail, 0, frame, 8, pduTail.Length);
        return frame;
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<byte[]> SendAndReceiveAsync(int port, byte[] request)
    {
        var response = await SendAndReceiveOptionalAsync(port, request, TimeSpan.FromSeconds(2));
        Assert.NotNull(response);
        return response;
    }

    private static async Task<byte[]?> SendAndReceiveOptionalAsync(int port, byte[] request, TimeSpan timeout)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var stream = client.GetStream();
        await stream.WriteAsync(request);
        await stream.FlushAsync();

        using var cts = new CancellationTokenSource(timeout);
        var header = await ReadExactOptionalAsync(stream, 6, cts.Token);
        if (header == null)
        {
            return null;
        }

        var length = (header[4] << 8) | header[5];
        var body = await ReadExactOptionalAsync(stream, length, cts.Token);
        if (body == null)
        {
            return null;
        }

        return header.Concat(body).ToArray();
    }

    private static async Task<byte[]?> ReadExactOptionalAsync(NetworkStream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var totalRead = 0;

        try
        {
            while (totalRead < count)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), cancellationToken);
                if (read == 0)
                {
                    return null;
                }

                totalRead += read;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
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
