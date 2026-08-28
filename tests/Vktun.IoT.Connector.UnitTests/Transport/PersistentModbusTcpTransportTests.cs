using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Vktun.IoT.Connector.Communication.Channels;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.Transport;

public class PersistentModbusTcpTransportTests
{
    [Fact]
    public async Task Mbap_FragmentedResponses_ShouldReuseOnePhysicalConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptedConnections = 0;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            Interlocked.Increment(ref acceptedConnections);
            await using var stream = client.GetStream();
            for (var i = 0; i < 2; i++)
            {
                var request = await ReadMbapAsync(stream);
                var response = new byte[] { request[0], request[1], 0, 0, 0, 5, request[6], request[7], 2, 0, (byte)(i + 1) };
                await stream.WriteAsync(response.AsMemory(0, 4));
                await stream.WriteAsync(response.AsMemory(4));
            }
        });

        await using var transport = new PersistentModbusTcpTransport(new TestConfigurationProvider());
        var first = await transport.SendAsync(MbapRequest(port, 1));
        var second = await transport.SendAsync(MbapRequest(port, 2));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(1, Volatile.Read(ref acceptedConnections));
        Assert.Equal((byte)1, first.Response![10]);
        Assert.Equal((byte)2, second.Response![10]);
        await server.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task RtuOverTcpWithCrc_ShouldAssembleAndValidateFragmentedFrame()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var request = new byte[8];
            await ReadExactlyAsync(stream, request);
            var response = AddCrc([1, 3, 2, 0, 42]);
            await stream.WriteAsync(response.AsMemory(0, 2));
            await stream.WriteAsync(response.AsMemory(2));
        });

        await using var transport = new PersistentModbusTcpTransport(new TestConfigurationProvider());
        var result = await transport.SendAsync(new PersistentModbusTcpRequest
        {
            Host = "127.0.0.1",
            Port = port,
            WireFormat = PersistentModbusTcpWireFormat.RtuOverTcpWithCrc,
            Payload = AddCrc([1, 3, 0, 0, 0, 1]),
            TimeoutMs = 1000
        });

        Assert.True(result.Succeeded);
        Assert.Equal(AddCrc([1, 3, 2, 0, 42]), result.Response);
        await server.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Timeout_ShouldDiscardConnection_BeforeTheNextRequestReconnects()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using (var first = await listener.AcceptTcpClientAsync())
            {
                await using var firstStream = first.GetStream();
                await ReadMbapAsync(firstStream);
                await Task.Delay(250);
            }

            using var second = await listener.AcceptTcpClientAsync();
            await using var secondStream = second.GetStream();
            var request = await ReadMbapAsync(secondStream);
            await secondStream.WriteAsync(new byte[] { request[0], request[1], 0, 0, 0, 5, request[6], request[7], 2, 0, 7 });
        });

        await using var transport = new PersistentModbusTcpTransport(new TestConfigurationProvider());
        var timedOut = await transport.SendAsync(MbapRequest(port, 1, 80));
        var recovered = await transport.SendAsync(MbapRequest(port, 2));

        Assert.Equal(PersistentModbusTcpOutcome.ResponseTimeout, timedOut.Outcome);
        Assert.True(recovered.Succeeded);
        Assert.Equal((byte)7, recovered.Response![10]);
        await server.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Probe_And_Send_ShouldShareOnePhysicalConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptedConnections = 0;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            Interlocked.Increment(ref acceptedConnections);
            await using var stream = client.GetStream();
            var request = await ReadMbapAsync(stream);
            var response = new byte[] { request[0], request[1], 0, 0, 0, 5, request[6], request[7], 2, 0, 42 };
            await stream.WriteAsync(response);
        });

        await using var transport = new PersistentModbusTcpTransport(new TestConfigurationProvider());
        var probe = await transport.ProbeAsync(MbapRequest(port, 1));
        var send = await transport.SendAsync(MbapRequest(port, 2));

        Assert.True(probe.Succeeded);
        Assert.True(send.Succeeded);
        Assert.Equal(1, Volatile.Read(ref acceptedConnections));
        await server.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Probe_WhenBusinessSendHoldsTheEndpointGate_ShouldQueueWaitTimeout()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var request = await ReadMbapAsync(stream);
            requestReceived.SetResult();
            await releaseResponse.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var response = new byte[] { request[0], request[1], 0, 0, 0, 5, request[6], request[7], 2, 0, 42 };
            await stream.WriteAsync(response);
        });

        await using var transport = new PersistentModbusTcpTransport(new TestConfigurationProvider(new SdkConfig
        {
            Tcp = new TcpConfig { ProbeGateWaitTimeoutMs = 50 }
        }));

        var sendTask = transport.SendAsync(MbapRequest(port, 1, 2000));
        await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var stopwatch = Stopwatch.StartNew();
        var probe = await transport.ProbeAsync(MbapRequest(port, 2, 2000));
        stopwatch.Stop();

        Assert.Equal(PersistentModbusTcpOutcome.ResponseTimeout, probe.Outcome);
        Assert.True(stopwatch.ElapsedMilliseconds < 500, $"probe should fail fast, took {stopwatch.ElapsedMilliseconds}ms");

        releaseResponse.SetResult();
        var send = await sendTask.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(send.Succeeded);

        // The queued probe did not destroy the shared connection; the next probe succeeds on it.
        var laterProbe = await transport.ProbeAsync(MbapRequest(port, 3, 2000));
        Assert.True(laterProbe.Succeeded);
        await server.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private static PersistentModbusTcpRequest MbapRequest(int port, ushort transactionId, int timeoutMs = 1000) => new()
    {
        Host = "127.0.0.1",
        Port = port,
        TimeoutMs = timeoutMs,
        Payload = [(byte)(transactionId >> 8), (byte)transactionId, 0, 0, 0, 6, 1, 3, 0, 0, 0, 1]
    };

    private static async Task<byte[]> ReadMbapAsync(Stream stream)
    {
        var header = new byte[6];
        await ReadExactlyAsync(stream, header);
        var length = (header[4] << 8) | header[5];
        var tail = new byte[length];
        await ReadExactlyAsync(stream, tail);
        return header.Concat(tail).ToArray();
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset));
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }
    }

    private static byte[] AddCrc(byte[] frame)
    {
        ushort crc = 0xFFFF;
        foreach (var value in frame)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
        }

        return frame.Concat([(byte)crc, (byte)(crc >> 8)]).ToArray();
    }

    private sealed class TestConfigurationProvider : IConfigurationProvider
    {
        private readonly SdkConfig _config;

        public TestConfigurationProvider()
            : this(new SdkConfig { Tcp = new TcpConfig { SessionIdleTimeout = 3_600_000 } })
        {
        }

        public TestConfigurationProvider(SdkConfig config)
        {
            _config = config;
        }

        public SdkConfig GetConfig() => _config;
        public Task<SdkConfig> LoadConfigAsync(string filePath) => Task.FromResult(_config);
        public Task SaveConfigAsync(string filePath, SdkConfig config) => Task.CompletedTask;
        public Task<bool> UpdateConfigAsync(Action<SdkConfig> updateAction) { updateAction(_config); return Task.FromResult(true); }
        public event EventHandler<ConfigChangedEventArgs>? ConfigChanged;
        public Task<List<ProtocolConfig>> LoadProtocolTemplatesAsync(string templatesDirectory) => Task.FromResult(new List<ProtocolConfig>());
        public Task<ProtocolConfig?> LoadProtocolTemplateAsync(string filePath) => Task.FromResult<ProtocolConfig?>(null);
        public Task<List<string>> GetProtocolTemplatePathsAsync(string templatesDirectory) => Task.FromResult(new List<string>());
        public Task SaveProtocolTemplateAsync(string filePath, ProtocolConfig config) => Task.CompletedTask;
        public Task<bool> ExportTemplateAsync(ProtocolConfig config, string exportPath) => Task.FromResult(true);
        public Task<ProtocolConfig?> ImportTemplateAsync(string importPath) => Task.FromResult<ProtocolConfig?>(null);
        public Task<ProtocolTemplateVersion?> GetTemplateVersionAsync(string filePath) => Task.FromResult<ProtocolTemplateVersion?>(null);
        public Task StartTemplateWatchAsync(string templatesDirectory, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ProtocolConfigValidationReport ValidateTemplate(ProtocolConfig config) => new() { IsValid = true };
        public Task<List<ProtocolConfigValidationReport>> ValidateAllTemplatesAsync(string templatesDirectory) => Task.FromResult(new List<ProtocolConfigValidationReport>());
    }
}
