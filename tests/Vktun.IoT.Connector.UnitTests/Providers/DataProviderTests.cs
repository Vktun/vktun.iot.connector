using Vktun.IoT.Connector.Business.Providers;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.Providers;

public class DataProviderTests
{
    [Fact]
    public async Task WriteDataAsync_WithMemoryPersistence_ShouldSeparateRecentAndHistoryValues()
    {
        var config = CreateConfig(DataPersistenceBackend.Memory);
        var cache = new DataCache(maxSize: 10);
        var store = new MemoryDataPersistenceStore();
        var provider = new DataProvider(cache, store, new TestConfigurationProvider(config), new TestLogger());
        var first = CreateData("device-1", "temperature", 21, new DateTime(2026, 4, 14, 8, 0, 0));
        var second = CreateData("device-1", "temperature", 22, new DateTime(2026, 4, 14, 8, 1, 0));

        var firstResult = await provider.WriteDataAsync(first);
        var secondResult = await provider.WriteDataAsync(second);
        var latest = await provider.ReadDataAsync("device-1", "temperature");
        var history = (await provider.ReadDataHistoryAsync(
            "device-1",
            first.CollectTime.AddSeconds(-1),
            second.CollectTime.AddSeconds(1))).ToArray();

        Assert.True(firstResult);
        Assert.True(secondResult);
        Assert.Same(second, latest);
        Assert.Equal(1, cache.Count);
        Assert.Equal(new[] { first.CollectTime, second.CollectTime }, history.Select(d => d.CollectTime));
    }

    [Fact]
    public async Task ReadReplayDataAsync_ShouldReturnOrderedLimitedPersistentValues()
    {
        var config = CreateConfig(DataPersistenceBackend.Memory);
        var provider = new DataProvider(
            new DataCache(maxSize: 10),
            new MemoryDataPersistenceStore(),
            new TestConfigurationProvider(config),
            new TestLogger());
        var first = CreateData("device-1", "temperature", 21, new DateTime(2026, 4, 14, 8, 0, 0));
        var second = CreateData("device-1", "temperature", 22, new DateTime(2026, 4, 14, 8, 1, 0));
        var third = CreateData("device-1", "temperature", 23, new DateTime(2026, 4, 14, 8, 2, 0));

        await provider.WriteDataBatchAsync(new[] { third, first, second });
        var replay = (await provider.ReadReplayDataAsync(
            "device-1",
            first.CollectTime.AddSeconds(-1),
            third.CollectTime.AddSeconds(1),
            maxCount: 2)).ToArray();

        Assert.Equal(new[] { first.CollectTime, second.CollectTime }, replay.Select(d => d.CollectTime));
    }

    [Fact]
    public async Task WriteDataAsync_WhenPersistentWriteRejectedAndCacheOnly_ShouldReturnTrueAndKeepRecentValue()
    {
        var config = CreateConfig(DataPersistenceBackend.External);
        config.Persistence.FailureStrategy = DataPersistenceFailureStrategy.CacheOnly;
        var provider = new DataProvider(
            new DataCache(maxSize: 10),
            new RejectingDataStore(),
            new TestConfigurationProvider(config),
            new TestLogger());
        var data = CreateData("device-1", "temperature", 21, DateTime.UtcNow);

        var result = await provider.WriteDataAsync(data);
        var latest = await provider.ReadDataAsync("device-1", "temperature");

        Assert.True(result);
        Assert.Same(data, latest);
    }

    [Fact]
    public async Task WriteDataAsync_WhenPersistentWriteRejectedAndRejectWrite_ShouldReturnFalse()
    {
        var config = CreateConfig(DataPersistenceBackend.External);
        config.Persistence.FailureStrategy = DataPersistenceFailureStrategy.RejectWrite;
        var provider = new DataProvider(
            new DataCache(maxSize: 10),
            new RejectingDataStore(),
            new TestConfigurationProvider(config),
            new TestLogger());

        var result = await provider.WriteDataAsync(CreateData("device-1", "temperature", 21, DateTime.UtcNow));

        Assert.False(result);
    }

    private static SdkConfig CreateConfig(DataPersistenceBackend backend)
    {
        return new SdkConfig
        {
            Global = new GlobalConfig
            {
                EnableDataCache = true,
                CacheMaxSize = 10
            },
            Persistence = new DataPersistenceConfig
            {
                Enabled = true,
                Backend = backend,
                EnableWriteBuffer = false,
                MaxHistoryItems = 1000,
                FailureStrategy = DataPersistenceFailureStrategy.CacheOnly
            }
        };
    }

    private static DeviceData CreateData(string deviceId, string pointName, int value, DateTime collectTime)
    {
        return new DeviceData
        {
            DeviceId = deviceId,
            ChannelId = "channel-1",
            ProtocolType = ProtocolType.ModbusTcp,
            CollectTime = collectTime,
            DataItems =
            [
                new DataPoint
                {
                    PointName = pointName,
                    Value = value,
                    DataType = DataType.Int32,
                    Timestamp = collectTime
                }
            ]
        };
    }

    private sealed class RejectingDataStore : IPersistentDataStore
    {
        public DataPersistenceBackend Backend => DataPersistenceBackend.External;

        public Task<bool> WriteAsync(DeviceData data, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<bool> WriteBatchAsync(IEnumerable<DeviceData> dataList, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<IReadOnlyList<DeviceData>> ReadAsync(
            string deviceId,
            DateTime startTime,
            DateTime endTime,
            DataCachePurpose purpose,
            int maxCount,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<DeviceData>>(Array.Empty<DeviceData>());
        }
    }

    private sealed class TestConfigurationProvider(SdkConfig config) : IConfigurationProvider
    {
        public event EventHandler<ConfigChangedEventArgs>? ConfigChanged;

        public SdkConfig GetConfig()
        {
            return config;
        }

        public Task<SdkConfig> LoadConfigAsync(string filePath)
        {
            return Task.FromResult(config);
        }

        public Task SaveConfigAsync(string filePath, SdkConfig config)
        {
            return Task.CompletedTask;
        }

        public Task<bool> UpdateConfigAsync(Action<SdkConfig> updateAction)
        {
            updateAction(config);
            ConfigChanged?.Invoke(this, new ConfigChangedEventArgs { NewConfig = config });
            return Task.FromResult(true);
        }

        public Task<List<ProtocolConfig>> LoadProtocolTemplatesAsync(string templatesDirectory)
        {
            throw new NotImplementedException();
        }

        public Task<ProtocolConfig?> LoadProtocolTemplateAsync(string filePath)
        {
            throw new NotImplementedException();
        }

        public Task<List<string>> GetProtocolTemplatePathsAsync(string templatesDirectory)
        {
            throw new NotImplementedException();
        }

        public Task SaveProtocolTemplateAsync(string filePath, ProtocolConfig config)
        {
            throw new NotImplementedException();
        }

        public Task<bool> ExportTemplateAsync(ProtocolConfig config, string exportPath)
        {
            throw new NotImplementedException();
        }

        public Task<ProtocolConfig?> ImportTemplateAsync(string importPath)
        {
            throw new NotImplementedException();
        }

        public Task<ProtocolTemplateVersion?> GetTemplateVersionAsync(string filePath)
        {
            throw new NotImplementedException();
        }

        public Task StartTemplateWatchAsync(string templatesDirectory, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ProtocolConfigValidationReport ValidateTemplate(ProtocolConfig config)
        {
            throw new NotImplementedException();
        }

        public Task<List<ProtocolConfigValidationReport>> ValidateAllTemplatesAsync(string templatesDirectory)
        {
            throw new NotImplementedException();
        }
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
