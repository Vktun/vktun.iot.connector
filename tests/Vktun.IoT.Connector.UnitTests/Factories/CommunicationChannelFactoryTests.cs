using Vktun.IoT.Connector.Business.Factories;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.Factories;

public class CommunicationChannelFactoryTests
{
    private readonly CommunicationChannelFactory _factory;

    public CommunicationChannelFactoryTests()
    {
        _factory = new CommunicationChannelFactory(new TestConfigurationProvider(), new TestLogger());
    }

    [Fact]
    public void CreateChannel_UdpClient_ShouldKeepClientMode()
    {
        var device = new DeviceInfo
        {
            DeviceId = "udp-client",
            CommunicationType = CommunicationType.Udp,
            ConnectionMode = ConnectionMode.Client,
            IpAddress = "127.0.0.1",
            Port = 12000
        };

        var channel = _factory.CreateChannel(device);

        Assert.Equal(ConnectionMode.Client, channel.ConnectionMode);
    }

    [Fact]
    public void CreateChannel_UdpServerWithLegacyPort_ShouldNormalizeToLocalPort()
    {
        var device = new DeviceInfo
        {
            DeviceId = "udp-server",
            CommunicationType = CommunicationType.Udp,
            ConnectionMode = ConnectionMode.Server,
            Port = 14000
        };

        var channel = _factory.CreateChannel(device);

        Assert.Equal(ConnectionMode.Server, channel.ConnectionMode);
        Assert.Equal(14000, device.LocalPort);
        Assert.Equal(0, device.Port);
    }

    [Fact]
    public void CreateChannel_HttpClient_ShouldCreateHttpChannel()
    {
        var device = new DeviceInfo
        {
            DeviceId = "http-client",
            CommunicationType = CommunicationType.Http,
            ConnectionMode = ConnectionMode.Client,
            IpAddress = "api.example.test",
            ExtendedProperties = new Dictionary<string, object>
            {
                ["Scheme"] = "https",
                ["Path"] = "/collect"
            }
        };

        var channel = _factory.CreateChannel(device);

        Assert.Equal(CommunicationType.Http, channel.CommunicationType);
        Assert.Equal(ConnectionMode.Client, channel.ConnectionMode);
        Assert.Equal("api.example.test", device.IpAddress);
    }

    [Fact]
    public void CreateChannel_MqttClient_ShouldCreateMqttChannel()
    {
        var device = new DeviceInfo
        {
            DeviceId = "mqtt-client",
            CommunicationType = CommunicationType.Mqtt,
            ConnectionMode = ConnectionMode.Client,
            IpAddress = "broker.example.test",
            Port = 1883,
            ExtendedProperties = new Dictionary<string, object>
            {
                ["SubscribeTopics"] = "devices/+/telemetry,devices/+/status",
                ["QosLevel"] = "1"
            }
        };

        var channel = _factory.CreateChannel(device);

        Assert.Equal(CommunicationType.Mqtt, channel.CommunicationType);
        Assert.Equal(ConnectionMode.Client, channel.ConnectionMode);
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
