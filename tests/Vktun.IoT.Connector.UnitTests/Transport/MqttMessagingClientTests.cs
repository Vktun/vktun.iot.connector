using System.Text;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
using Vktun.IoT.Connector.Communication.Channels;
using Vktun.IoT.Connector.Communication.Mqtt;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.Transport;

public class MqttMessagingClientTests
{
    private readonly IConfigurationProvider _configProvider = new TestConfigurationProvider();
    private readonly TestLogger _logger = new();

    [Theory]
    [InlineData("devices/+/telemetry", "devices/001/telemetry", true)]
    [InlineData("devices/#", "devices/001/telemetry", true)]
    [InlineData("devices/+/status", "devices/001/telemetry", false)]
    public void MqttTopicFilter_IsMatch_ShouldSupportMqttWildcards(string filter, string topic, bool expected)
    {
        Assert.Equal(expected, MqttTopicFilter.IsMatch(filter, topic));
    }

    [Fact]
    public async Task MqttMessagingClient_ShouldPublishAndDispatchSubscribedMessages()
    {
        var mqttConfig = new MqttConfig
        {
            Server = "broker.example.test",
            ClientId = "test-client",
            QosLevel = 1,
            UseInMemoryTransport = true
        };
        await using var client = new MqttMessagingClient(_configProvider, _logger, mqttConfig);
        var receivedTcs = new TaskCompletionSource<MqttReceivedMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await client.ConnectAsync();
        await client.SubscribeAsync(new MqttSubscribeOptions
        {
            TopicFilter = "devices/+/telemetry",
            Qos = MqttQualityOfService.AtLeastOnce
        }, (message, _) =>
        {
            receivedTcs.TrySetResult(message);
            return Task.CompletedTask;
        });

        await client.PublishStringAsync("devices/001/telemetry", "{\"temperature\":21}", MqttQualityOfService.AtLeastOnce);

        var received = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("devices/001/telemetry", received.Topic);
        Assert.Equal("{\"temperature\":21}", Encoding.UTF8.GetString(received.Payload));
        Assert.Equal(MqttQualityOfService.AtLeastOnce, received.Qos);
    }

    [Fact]
    public async Task MqttMessagingClient_ShouldPublishAndReceiveThroughLocalBroker()
    {
        var port = GetFreeTcpPort();
        var mqttFactory = new MqttFactory();
        var serverOptions = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(port)
            .Build();
        var server = mqttFactory.CreateMqttServer(serverOptions);

        await server.StartAsync();
        try
        {
            var mqttConfig = new MqttConfig
            {
                Server = "127.0.0.1",
                Port = port,
                ClientId = $"test-client-{Guid.NewGuid():N}",
                QosLevel = 1
            };
            await using var client = new MqttMessagingClient(_configProvider, _logger, mqttConfig);
            var receivedTcs = new TaskCompletionSource<MqttReceivedMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

            await client.ConnectAsync();
            await client.SubscribeAsync(new MqttSubscribeOptions
            {
                TopicFilter = "devices/+/telemetry",
                Qos = MqttQualityOfService.AtLeastOnce
            }, (message, _) =>
            {
                receivedTcs.TrySetResult(message);
                return Task.CompletedTask;
            });

            await client.PublishStringAsync("devices/002/telemetry", "{\"humidity\":50}", MqttQualityOfService.AtLeastOnce);

            var received = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("devices/002/telemetry", received.Topic);
            Assert.Equal("{\"humidity\":50}", Encoding.UTF8.GetString(received.Payload));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task MqttMessagingClient_ConnectAsync_ShouldThrowWhenBrokerIsUnavailable()
    {
        var mqttConfig = new MqttConfig
        {
            Server = "127.0.0.1",
            Port = GetFreeTcpPort(),
            ClientId = $"unavailable-client-{Guid.NewGuid():N}",
            AutoReconnect = false
        };
        await using var client = new MqttMessagingClient(_configProvider, _logger, mqttConfig);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task MqttMessagingClient_ConnectAsync_ShouldThrowWhenBrokerRejectsAuthentication()
    {
        var port = GetFreeTcpPort();
        var mqttFactory = new MqttFactory();
        var serverOptions = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(port)
            .Build();
        var server = mqttFactory.CreateMqttServer(serverOptions);
        server.ValidatingConnectionAsync += args =>
        {
            args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
            return Task.CompletedTask;
        };

        await server.StartAsync();
        try
        {
            var mqttConfig = new MqttConfig
            {
                Server = "127.0.0.1",
                Port = port,
                ClientId = $"auth-failure-client-{Guid.NewGuid():N}",
                Username = "bad-user",
                Password = "bad-password",
                AutoReconnect = false
            };
            await using var client = new MqttMessagingClient(_configProvider, _logger, mqttConfig);

            await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());
            Assert.False(client.IsConnected);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task MqttMessagingClient_SubscribeAsync_ShouldThrowBeforeConnect()
    {
        var mqttConfig = new MqttConfig
        {
            Server = "127.0.0.1",
            Port = GetFreeTcpPort(),
            ClientId = $"subscribe-failure-client-{Guid.NewGuid():N}",
            AutoReconnect = false
        };
        await using var client = new MqttMessagingClient(_configProvider, _logger, mqttConfig);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SubscribeAsync(new MqttSubscribeOptions
        {
            TopicFilter = "devices/+/telemetry",
            Qos = MqttQualityOfService.AtLeastOnce
        }, (_, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task MqttMessagingClient_ShouldRestoreSubscriptionAfterReconnect()
    {
        var port = GetFreeTcpPort();
        var mqttFactory = new MqttFactory();
        var serverOptions = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(port)
            .Build();
        var server = mqttFactory.CreateMqttServer(serverOptions);

        await server.StartAsync();
        try
        {
            var mqttConfig = new MqttConfig
            {
                Server = "127.0.0.1",
                Port = port,
                ClientId = $"reconnect-client-{Guid.NewGuid():N}",
                QosLevel = 1,
                AutoReconnect = true,
                ReconnectDelay = 100,
                KeepAlivePeriod = 1
            };
            await using var client = new MqttMessagingClient(_configProvider, _logger, mqttConfig);
            var receivedTcs = new TaskCompletionSource<MqttReceivedMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

            await client.ConnectAsync();
            await client.SubscribeAsync(new MqttSubscribeOptions
            {
                TopicFilter = "devices/+/telemetry",
                Qos = MqttQualityOfService.AtLeastOnce
            }, (message, _) =>
            {
                receivedTcs.TrySetResult(message);
                return Task.CompletedTask;
            });

            await server.DisconnectClientAsync(mqttConfig.ClientId);
            await WaitUntilAsync(() => !client.IsConnected, TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => client.IsConnected, TimeSpan.FromSeconds(5));

            await client.PublishStringAsync("devices/003/telemetry", "{\"pressure\":101}", MqttQualityOfService.AtLeastOnce);

            var received = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("devices/003/telemetry", received.Topic);
            Assert.Equal("{\"pressure\":101}", Encoding.UTF8.GetString(received.Payload));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        while (!condition())
        {
            await Task.Delay(50, timeoutCts.Token);
        }
    }

    private sealed class TestConfigurationProvider : IConfigurationProvider
    {
        private readonly SdkConfig _config = new();

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
