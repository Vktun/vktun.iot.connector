using System.Net;
using System.Net.Sockets;
using System.Text;
using Vktun.IoT.Connector.Communication.Channels;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.Transport;

public class SocketChannelIntegrationTests
{
    private readonly IConfigurationProvider _configProvider = new TestConfigurationProvider();
    private readonly ILogger _logger = new TestLogger();

    [Fact]
    public async Task TcpClientChannel_ShouldSendAndReceive()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        await using var channel = new TcpClientChannel(_configProvider, _logger);
        var receivedTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.DataReceived += (_, args) => receivedTcs.TrySetResult(args.Data);

        var device = new DeviceInfo
        {
            DeviceId = "tcp-client-device",
            CommunicationType = CommunicationType.Tcp,
            ConnectionMode = ConnectionMode.Client,
            IpAddress = "127.0.0.1",
            Port = port
        };

        Assert.True(await channel.OpenAsync());

        var acceptTask = listener.AcceptTcpClientAsync();
        Assert.True(await channel.ConnectDeviceAsync(device));

        using var acceptedClient = await acceptTask;
        using var stream = acceptedClient.GetStream();

        var inbound = Encoding.UTF8.GetBytes("from-server");
        await stream.WriteAsync(inbound);
        var received = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(inbound, received);

        var outbound = Encoding.UTF8.GetBytes("from-client");
        var sent = await channel.SendAsync(device.DeviceId, outbound);
        Assert.Equal(outbound.Length, sent);

        var readBuffer = new byte[64];
        var read = await stream.ReadAsync(readBuffer);
        Assert.Equal(outbound, readBuffer.Take(read).ToArray());
    }

    [Fact]
    public async Task TcpServerChannel_ShouldWaitAndBindConfiguredDevice()
    {
        var port = GetFreeTcpPort();
        await using var channel = new TcpServerChannel(string.Empty, port, _configProvider, _logger);
        var receivedTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.DataReceived += (_, args) => receivedTcs.TrySetResult(args.Data);

        var device = new DeviceInfo
        {
            DeviceId = "tcp-server-device",
            CommunicationType = CommunicationType.Tcp,
            ConnectionMode = ConnectionMode.Server,
            LocalPort = port,
            IpAddress = "127.0.0.1"
        };

        Assert.True(await channel.OpenAsync());

        var connectTask = channel.ConnectDeviceAsync(device);
        using var remoteClient = new TcpClient();
        await remoteClient.ConnectAsync(IPAddress.Loopback, port);
        Assert.True(await connectTask.WaitAsync(TimeSpan.FromSeconds(3)));

        var stream = remoteClient.GetStream();
        var inbound = Encoding.UTF8.GetBytes("hello-server");
        await stream.WriteAsync(inbound);
        var received = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(inbound, received);

        var outbound = Encoding.UTF8.GetBytes("hello-client");
        var sent = await channel.SendAsync(device.DeviceId, outbound);
        Assert.Equal(outbound.Length, sent);

        var readBuffer = new byte[64];
        var read = await stream.ReadAsync(readBuffer);
        Assert.Equal(outbound, readBuffer.Take(read).ToArray());
    }

    [Fact]
    public async Task UdpClientChannel_ShouldSendAndReceive()
    {
        using var remote = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var remoteEndpoint = (IPEndPoint)remote.Client.LocalEndPoint!;

        await using var channel = new UdpChannel(ConnectionMode.Client, string.Empty, 0, _configProvider, _logger);
        var receivedTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.DataReceived += (_, args) => receivedTcs.TrySetResult(args.Data);

        var device = new DeviceInfo
        {
            DeviceId = "udp-client-device",
            CommunicationType = CommunicationType.Udp,
            ConnectionMode = ConnectionMode.Client,
            IpAddress = "127.0.0.1",
            Port = remoteEndpoint.Port
        };

        Assert.True(await channel.OpenAsync());
        Assert.True(await channel.ConnectDeviceAsync(device));

        var outbound = Encoding.UTF8.GetBytes("udp-client-out");
        var sent = await channel.SendAsync(device.DeviceId, outbound);
        Assert.Equal(outbound.Length, sent);

        var serverReceive = await remote.ReceiveAsync();
        Assert.Equal(outbound, serverReceive.Buffer);

        var inbound = Encoding.UTF8.GetBytes("udp-client-in");
        await remote.SendAsync(inbound, inbound.Length, serverReceive.RemoteEndPoint);
        var received = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(inbound, received);
    }

    [Fact]
    public async Task UdpServerChannel_ShouldBindOnFirstPacketAndSendBack()
    {
        var port = GetFreeUdpPort();
        await using var channel = new UdpChannel(ConnectionMode.Server, string.Empty, port, _configProvider, _logger);
        var receivedTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.DataReceived += (_, args) =>
        {
            if (Encoding.UTF8.GetString(args.Data) == "payload-two")
            {
                receivedTcs.TrySetResult(args.Data);
            }
        };

        var device = new DeviceInfo
        {
            DeviceId = "udp-server-device",
            CommunicationType = CommunicationType.Udp,
            ConnectionMode = ConnectionMode.Server,
            LocalPort = port,
            IpAddress = "127.0.0.1"
        };

        Assert.True(await channel.OpenAsync());

        using var remote = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var connectTask = channel.ConnectDeviceAsync(device);

        var firstPayload = Encoding.UTF8.GetBytes("payload-one");
        await remote.SendAsync(firstPayload, firstPayload.Length, new IPEndPoint(IPAddress.Loopback, port));
        Assert.True(await connectTask.WaitAsync(TimeSpan.FromSeconds(3)));

        var secondPayload = Encoding.UTF8.GetBytes("payload-two");
        await remote.SendAsync(secondPayload, secondPayload.Length, new IPEndPoint(IPAddress.Loopback, port));
        var received = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(secondPayload, received);

        var outbound = Encoding.UTF8.GetBytes("reply");
        var sent = await channel.SendAsync(device.DeviceId, outbound);
        Assert.Equal(outbound.Length, sent);

        var reply = await remote.ReceiveAsync();
        Assert.Equal(outbound, reply.Buffer);
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static int GetFreeUdpPort()
    {
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)client.Client.LocalEndPoint!).Port;
    }

    private sealed class TestConfigurationProvider : IConfigurationProvider
    {
        private readonly SdkConfig _config = new()
        {
            Global = new GlobalConfig
            {
                ConnectionTimeout = 3000
            }
        };

        public SdkConfig GetConfig() => _config;
        public Task<SdkConfig> LoadConfigAsync(string filePath) => Task.FromResult(_config);
        public Task SaveConfigAsync(string filePath, SdkConfig config) => Task.CompletedTask;
        public Task<bool> UpdateConfigAsync(Action<SdkConfig> updateAction)
        {
            updateAction(_config);
            return Task.FromResult(true);
        }

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

    private sealed class TestLogger : ILogger
    {
        public void Log(LogLevel level, string message, Exception? exception = null)
        {
        }

        public void Debug(string message)
        {
        }

        public void Info(string message)
        {
        }

        public void Warning(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }

        public void Fatal(string message, Exception? exception = null)
        {
        }
    }
}


