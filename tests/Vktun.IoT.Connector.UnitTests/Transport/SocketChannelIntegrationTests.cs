using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
    private readonly TestLogger _logger = new();

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
    public async Task TcpClientChannel_CloseAsync_ShouldNotLogReceiveErrorWhenReceiveLoopIsCanceled()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        await using var channel = new TcpClientChannel(_configProvider, _logger);
        var device = new DeviceInfo
        {
            DeviceId = "tcp-close-device",
            CommunicationType = CommunicationType.Tcp,
            ConnectionMode = ConnectionMode.Client,
            IpAddress = "127.0.0.1",
            Port = port
        };

        Assert.True(await channel.OpenAsync());

        var acceptTask = listener.AcceptTcpClientAsync();
        Assert.True(await channel.ConnectDeviceAsync(device));

        using var acceptedClient = await acceptTask;
        await Task.Delay(100);

        await channel.CloseAsync();

        Assert.DoesNotContain(
            _logger.ErrorMessages,
            message => message.Contains("TCP receive failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TcpClientChannel_SendAndReceiveAsync_WhenResponseTimesOut_ShouldNotLogReceiveError()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        await using var channel = new TcpClientChannel(_configProvider, _logger);
        var device = new DeviceInfo
        {
            DeviceId = "tcp-timeout-device",
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

        var outbound = Encoding.UTF8.GetBytes("timeout-request");
        var sendReceiveTask = channel.SendAndReceiveAsync(device.DeviceId, outbound, timeoutMs: 100);

        var readBuffer = new byte[64];
        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var read = await stream.ReadAsync(readBuffer, readCts.Token);
        Assert.Equal(outbound, readBuffer.Take(read).ToArray());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sendReceiveTask);
        await channel.CloseAsync();

        Assert.DoesNotContain(
            _logger.ErrorMessages,
            message => message.Contains("TCP receive failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TcpClientChannel_WhenRemoteDisconnects_ShouldNotLogReceiveErrorOrRaiseChannelError()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        await using var channel = new TcpClientChannel(_configProvider, _logger);
        var disconnectedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorTcs = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.DeviceDisconnected += (_, args) => disconnectedTcs.TrySetResult(args.Reason);
        channel.ErrorOccurred += (_, args) => errorTcs.TrySetResult(args.Exception);

        var device = new DeviceInfo
        {
            DeviceId = "tcp-remote-close-device",
            CommunicationType = CommunicationType.Tcp,
            ConnectionMode = ConnectionMode.Client,
            IpAddress = "127.0.0.1",
            Port = port
        };

        Assert.True(await channel.OpenAsync());

        var acceptTask = listener.AcceptTcpClientAsync();
        Assert.True(await channel.ConnectDeviceAsync(device));

        using var acceptedClient = await acceptTask;
        acceptedClient.Close();

        var reason = await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal("Remote disconnected", reason);
        Assert.False(errorTcs.Task.IsCompleted);
        Assert.DoesNotContain(
            _logger.ErrorMessages,
            message => message.Contains("TCP receive failed", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public async Task HttpClientChannel_SendAsync_ShouldUseInjectedFactoryAndRaiseDataReceived()
    {
        var responsePayload = Encoding.UTF8.GetBytes("{\"ok\":true}");
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(responsePayload)
        });
        using var httpClient = new HttpClient(handler);
        await using var channel = new HttpClientChannel(_configProvider, _logger, new TestHttpClientFactory(httpClient));
        var receivedTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.DataReceived += (_, args) => receivedTcs.TrySetResult(args.Data);

        var device = new DeviceInfo
        {
            DeviceId = "http-device",
            CommunicationType = CommunicationType.Http,
            ConnectionMode = ConnectionMode.Client,
            ExtendedProperties = new Dictionary<string, object>
            {
                ["Url"] = "https://api.example.test/devices/http-device",
                ["Method"] = "PUT",
                ["ContentType"] = "application/json",
                ["Headers"] = new Dictionary<string, string>
                {
                    ["X-Device"] = "http-device"
                }
            }
        };

        Assert.True(await channel.OpenAsync());
        Assert.True(await channel.ConnectDeviceAsync(device));

        var requestPayload = Encoding.UTF8.GetBytes("{\"value\":42}");
        var sent = await channel.SendAsync(device.DeviceId, requestPayload);
        var received = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(requestPayload.Length, sent);
        Assert.Equal(responsePayload, received);
        Assert.Equal(HttpMethod.Put, handler.RequestMethod);
        Assert.Equal("https://api.example.test/devices/http-device", handler.RequestUri?.ToString());
        Assert.Equal(requestPayload, handler.RequestBody);
        Assert.True(handler.RequestHeaders.TryGetValues("X-Device", out var headerValues));
        Assert.Contains("http-device", headerValues);
    }

    [Fact]
    public async Task HttpClientChannel_SendAsync_ShouldReturnZeroAndRaiseErrorForNonSuccessStatus()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            ReasonPhrase = "server-error",
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("failed"))
        });
        using var httpClient = new HttpClient(handler);
        await using var channel = new HttpClientChannel(_configProvider, _logger, new TestHttpClientFactory(httpClient));
        var errorTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ErrorOccurred += (_, args) => errorTcs.TrySetResult(args.Message);

        var device = CreateHttpDevice("http-non-success");

        Assert.True(await channel.OpenAsync());
        Assert.True(await channel.ConnectDeviceAsync(device));

        var sent = await channel.SendAsync(device.DeviceId, Encoding.UTF8.GetBytes("{}"));
        var error = await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(0, sent);
        Assert.Contains("500", error);
        Assert.Equal(1, channel.Statistics.TotalErrors);
    }

    [Fact]
    public async Task HttpClientChannel_SendAsync_ShouldRaiseDataReceivedForEmptyResponseBody()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        });
        using var httpClient = new HttpClient(handler);
        await using var channel = new HttpClientChannel(_configProvider, _logger, new TestHttpClientFactory(httpClient));
        var receivedTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.DataReceived += (_, args) => receivedTcs.TrySetResult(args.Data);

        var device = CreateHttpDevice("http-empty-response");

        Assert.True(await channel.OpenAsync());
        Assert.True(await channel.ConnectDeviceAsync(device));

        var requestPayload = Encoding.UTF8.GetBytes("ping");
        var sent = await channel.SendAsync(device.DeviceId, requestPayload);
        var received = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(requestPayload.Length, sent);
        Assert.Empty(received);
    }

    [Fact]
    public async Task HttpClientChannel_SendAsync_ShouldReturnZeroWhenCancellationRequested()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("late"))
        }, TimeSpan.FromSeconds(5));
        using var httpClient = new HttpClient(handler);
        await using var channel = new HttpClientChannel(_configProvider, _logger, new TestHttpClientFactory(httpClient));
        var device = CreateHttpDevice("http-canceled");

        Assert.True(await channel.OpenAsync());
        Assert.True(await channel.ConnectDeviceAsync(device));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sent = await channel.SendAsync(device.DeviceId, Encoding.UTF8.GetBytes("payload"), cts.Token);

        Assert.Equal(0, sent);
        Assert.Equal(0, channel.Statistics.TotalErrors);
    }

    [Fact]
    public async Task HttpClientChannel_SendAsync_ShouldReturnZeroWhenRequestTimeoutExpires()
    {
        var configProvider = new TestConfigurationProvider(new SdkConfig
        {
            Global = new GlobalConfig { ConnectionTimeout = 3000 },
            Http = new HttpConfig { RequestTimeout = 50 }
        });
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("late"))
        }, TimeSpan.FromSeconds(5));
        using var httpClient = new HttpClient(handler);
        await using var channel = new HttpClientChannel(configProvider, _logger, new TestHttpClientFactory(httpClient));
        var device = CreateHttpDevice("http-timeout");

        Assert.True(await channel.OpenAsync());
        Assert.True(await channel.ConnectDeviceAsync(device));

        var sent = await channel.SendAsync(device.DeviceId, Encoding.UTF8.GetBytes("payload"));

        Assert.Equal(0, sent);
        Assert.Equal(0, channel.Statistics.TotalErrors);
    }

    [Fact]
    public async Task HttpClientChannel_SendAsync_ShouldSupportConcurrentRequests()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("ok"))
        }, TimeSpan.FromMilliseconds(50));
        using var httpClient = new HttpClient(handler);
        await using var channel = new HttpClientChannel(_configProvider, _logger, new TestHttpClientFactory(httpClient));
        var device = CreateHttpDevice("http-concurrent");

        Assert.True(await channel.OpenAsync());
        Assert.True(await channel.ConnectDeviceAsync(device));

        var payload = Encoding.UTF8.GetBytes("payload");
        var sends = Enumerable.Range(0, 20)
            .Select(_ => channel.SendAsync(device.DeviceId, payload))
            .ToArray();
        var results = await Task.WhenAll(sends);

        Assert.All(results, result => Assert.Equal(payload.Length, result));
        Assert.Equal(20, handler.RequestCount);
        Assert.True(handler.MaxConcurrentRequests > 1);
    }

    private static DeviceInfo CreateHttpDevice(string deviceId)
    {
        return new DeviceInfo
        {
            DeviceId = deviceId,
            CommunicationType = CommunicationType.Http,
            ConnectionMode = ConnectionMode.Client,
            ExtendedProperties = new Dictionary<string, object>
            {
                ["Url"] = $"https://api.example.test/devices/{deviceId}",
                ["Method"] = "POST"
            }
        };
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
        private readonly SdkConfig _config;

        public TestConfigurationProvider()
            : this(new SdkConfig
            {
                Global = new GlobalConfig
                {
                    ConnectionTimeout = 3000
                }
            })
        {
        }

        public TestConfigurationProvider(SdkConfig config)
        {
            _config = config;
        }

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
        private readonly object _gate = new();

        public List<string> ErrorMessages { get; } = [];

        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            if (level < LogLevel.Error)
            {
                return;
            }

            lock (_gate)
            {
                ErrorMessages.Add(message);
            }
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
            Log(LogLevel.Error, message, exception);
        }

        public void Fatal(string message, Exception? exception = null)
        {
            Log(LogLevel.Fatal, message, exception);
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _httpClient;

        public TestHttpClientFactory(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public HttpClient CreateClient(string name)
        {
            return _httpClient;
        }
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;
        private readonly TimeSpan _delay;
        private int _activeRequests;
        private int _maxConcurrentRequests;
        private int _requestCount;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory, TimeSpan delay = default)
        {
            _responseFactory = responseFactory;
            _delay = delay;
        }

        public HttpMethod? RequestMethod { get; private set; }
        public Uri? RequestUri { get; private set; }
        public byte[] RequestBody { get; private set; } = Array.Empty<byte>();
        public HttpRequestHeaders RequestHeaders { get; private set; } = default!;
        public int RequestCount => Volatile.Read(ref _requestCount);
        public int MaxConcurrentRequests => Volatile.Read(ref _maxConcurrentRequests);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var activeRequests = Interlocked.Increment(ref _activeRequests);
            Interlocked.Increment(ref _requestCount);
            UpdateMaxConcurrentRequests(activeRequests);
            try
            {
                RequestMethod = request.Method;
                RequestUri = request.RequestUri;
                RequestHeaders = request.Headers;
                RequestBody = request.Content == null
                    ? Array.Empty<byte>()
                    : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

                if (_delay > TimeSpan.Zero)
                {
                    await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
                }

                return _responseFactory(request);
            }
            finally
            {
                Interlocked.Decrement(ref _activeRequests);
            }
        }

        private void UpdateMaxConcurrentRequests(int activeRequests)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maxConcurrentRequests);
                if (activeRequests <= current ||
                    Interlocked.CompareExchange(ref _maxConcurrentRequests, activeRequests, current) == current)
                {
                    return;
                }
            }
        }
    }
}


