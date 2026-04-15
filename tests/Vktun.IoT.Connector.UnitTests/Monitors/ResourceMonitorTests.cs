using Vktun.IoT.Connector.Concurrency.Monitors;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.Monitors;

public class ResourceMonitorTests
{
    [Fact]
    public void RecordOperation_ShouldUpdateDeviceProtocolMetricsAndDiagnosticTrace()
    {
        var monitor = CreateMonitor();
        var device = CreateDevice();
        monitor.RegisterDevice(device, "channel-1", "modbus-main", ProtocolType.ModbusTcp);

        monitor.RecordOperation(new ResourceOperationRecord
        {
            DeviceId = device.DeviceId,
            ChannelId = "channel-1",
            ProtocolId = "modbus-main",
            ProtocolType = ProtocolType.ModbusTcp,
            TaskId = "task-1",
            Success = true,
            ElapsedTime = TimeSpan.FromMilliseconds(25),
            ConfigVersion = 7,
            RequestBytes = 2,
            ResponseBytes = 1,
            RequestFrame = new byte[] { 0x01, 0x03 },
            ResponseFrame = new byte[] { 0x02 }
        });

        var snapshot = monitor.GetSnapshot();

        Assert.True(snapshot.DeviceMetrics.TryGetValue("device-1", out var deviceMetrics));
        Assert.Equal(1, deviceMetrics.TotalRequests);
        Assert.Equal(1, deviceMetrics.SuccessfulRequests);
        Assert.Equal(1, deviceMetrics.SlowRequests);

        Assert.True(snapshot.ProtocolMetrics.TryGetValue("modbus-main", out var protocolMetrics));
        Assert.Equal(1, protocolMetrics.TotalRequests);

        var trace = Assert.Single(snapshot.RecentDiagnostics);
        Assert.Equal("task-1", trace.TaskId);
        Assert.Equal("0103", trace.RequestFrameHex);
        Assert.Equal("02", trace.ResponseFrameHex);
        Assert.Equal(7, trace.ConfigVersion);
    }

    [Fact]
    public void GetSnapshot_ShouldUseTrackedChannelConnectionAndByteCounters()
    {
        var monitor = CreateMonitor();
        var channel = new TestChannel
        {
            ActiveConnectionCount = 2,
            CurrentStatistics = new ChannelStatistics
            {
                TotalBytesSent = 100,
                TotalBytesReceived = 50
            }
        };

        monitor.TrackChannel(channel);

        var snapshot = monitor.GetSnapshot();

        Assert.Equal(2, snapshot.ActiveConnections);
        Assert.Equal(2, snapshot.SocketHandleCount);
        Assert.Equal(100, snapshot.TotalBytesSent);
        Assert.Equal(50, snapshot.TotalBytesReceived);
        Assert.True(snapshot.Throughput > 0);
    }

    private static ResourceMonitor CreateMonitor()
    {
        var config = new SdkConfig
        {
            Resource = new ResourceConfig
            {
                SlowRequestThresholdMs = 10,
                MaxDiagnosticTraces = 16
            }
        };

        return new ResourceMonitor(new TestConfigurationProvider(config), new TestLogger());
    }

    private static DeviceInfo CreateDevice()
    {
        return new DeviceInfo
        {
            DeviceId = "device-1",
            ChannelId = "channel-1",
            ProtocolId = "modbus-main",
            ProtocolType = ProtocolType.ModbusTcp,
            CommunicationType = CommunicationType.Tcp,
            ConnectionMode = ConnectionMode.Client
        };
    }

    private sealed class TestChannel : ICommunicationChannel
    {
        public string ChannelId { get; set; } = "channel-1";
        public CommunicationType CommunicationType => CommunicationType.Tcp;
        public ConnectionMode ConnectionMode => ConnectionMode.Client;
        public bool IsConnected => true;
        public int ActiveConnectionCount { get; set; }
        public int ActiveConnections => ActiveConnectionCount;
        public ChannelStatistics CurrentStatistics { get; set; } = new();
        public ChannelStatistics Statistics => CurrentStatistics.Snapshot();

        public event EventHandler<ChannelErrorEventArgs>? ErrorOccurred;
        public event EventHandler<DeviceConnectedEventArgs>? DeviceConnected;
        public event EventHandler<DeviceDisconnectedEventArgs>? DeviceDisconnected;
        public event EventHandler<DataReceivedEventArgs>? DataReceived;
        public event EventHandler<DataSentEventArgs>? DataSent;

        public Task<bool> OpenAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task CloseAsync() => Task.CompletedTask;
        public Task<int> SendAsync(string deviceId, byte[] data, CancellationToken cancellationToken = default) => Task.FromResult(data.Length);
        public Task<int> SendAsync(string deviceId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) => Task.FromResult(data.Length);
        public async IAsyncEnumerable<ReceivedData> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<bool> ConnectDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task DisconnectDeviceAsync(string deviceId) => Task.CompletedTask;
        public ChannelStatistics ResetStatistics() => CurrentStatistics;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose()
        {
        }
    }

    private sealed class TestConfigurationProvider : IConfigurationProvider
    {
        private readonly SdkConfig _config;

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
