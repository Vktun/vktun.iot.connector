using System.Collections.Concurrent;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Business.Providers;

/// <summary>
/// In-memory recent-value cache keyed by device identifier.
/// </summary>
public class DataCache : IDataCache
{
    private readonly ConcurrentDictionary<string, DeviceData> _cacheByDeviceId;
    private readonly ConcurrentQueue<DeviceData> _orderedCache;
    private readonly object _lockObject = new();

    /// <inheritdoc />
    public int Count => _cacheByDeviceId.Count;

    /// <inheritdoc />
    public long MaxSize { get; set; }

    /// <inheritdoc />
    public bool IsFull => Count >= MaxSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataCache"/> class.
    /// </summary>
    /// <param name="maxSize">The maximum number of devices retained in the recent-value cache.</param>
    public DataCache(long maxSize = 10000)
    {
        _cacheByDeviceId = new ConcurrentDictionary<string, DeviceData>();
        _orderedCache = new ConcurrentQueue<DeviceData>();
        MaxSize = maxSize;
    }

    /// <inheritdoc />
    public void Add(DeviceData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        lock (_lockObject)
        {
            if (IsFull && !_cacheByDeviceId.ContainsKey(data.DeviceId))
            {
                if (_orderedCache.TryDequeue(out var oldest))
                {
                    _cacheByDeviceId.TryRemove(oldest.DeviceId, out _);
                }
            }

            if (_cacheByDeviceId.ContainsKey(data.DeviceId))
            {
                _cacheByDeviceId[data.DeviceId] = data;
            }
            else
            {
                _cacheByDeviceId[data.DeviceId] = data;
                _orderedCache.Enqueue(data);
            }
        }
    }

    /// <inheritdoc />
    public void AddRange(IEnumerable<DeviceData> dataList)
    {
        ArgumentNullException.ThrowIfNull(dataList);

        foreach (var data in dataList)
        {
            Add(data);
        }
    }

    /// <inheritdoc />
    public DeviceData? Get(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        _cacheByDeviceId.TryGetValue(deviceId, out var data);
        return data;
    }

    /// <inheritdoc />
    public IEnumerable<DeviceData> GetAll()
    {
        return _cacheByDeviceId.Values.ToArray();
    }

    /// <inheritdoc />
    public IEnumerable<DeviceData> GetByDevice(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        _cacheByDeviceId.TryGetValue(deviceId, out var data);
        return data != null ? new[] { data } : Array.Empty<DeviceData>();
    }

    /// <inheritdoc />
    public bool Remove(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        return _cacheByDeviceId.TryRemove(deviceId, out _);
    }

    /// <inheritdoc />
    public void Clear()
    {
        _cacheByDeviceId.Clear();
        while (_orderedCache.TryDequeue(out _))
        {
        }
    }
}

/// <summary>
/// Default data provider that separates recent-value caching from optional persistent storage.
/// </summary>
public class DataProvider : IDataProvider
{
    private readonly IDataCache _cache;
    private readonly IPersistentDataStore _persistentStore;
    private readonly IConfigurationProvider? _configProvider;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataProvider"/> class with only a recent-value cache.
    /// </summary>
    /// <param name="cache">The recent-value cache.</param>
    /// <param name="logger">The logger.</param>
    public DataProvider(IDataCache cache, ILogger logger)
        : this(cache, new NoopDataPersistenceStore(), null, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DataProvider"/> class.
    /// </summary>
    /// <param name="cache">The recent-value cache.</param>
    /// <param name="persistentStore">The historical and replay value store.</param>
    /// <param name="configProvider">The configuration provider.</param>
    /// <param name="logger">The logger.</param>
    public DataProvider(
        IDataCache cache,
        IPersistentDataStore persistentStore,
        IConfigurationProvider? configProvider,
        ILogger logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _persistentStore = persistentStore ?? throw new ArgumentNullException(nameof(persistentStore));
        _configProvider = configProvider;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public event EventHandler<DataWrittenEventArgs>? DataWritten;

    /// <inheritdoc />
    public async Task<bool> WriteDataAsync(DeviceData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            AddToRecentCache(data);

            if (!await PersistDataAsync(data).ConfigureAwait(false))
            {
                return false;
            }

            RaiseDataWritten(data);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to write device data: {ex.Message}", ex);

            if (GetPersistenceConfig().FailureStrategy == DataPersistenceFailureStrategy.Throw)
            {
                throw;
            }

            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> WriteDataBatchAsync(IEnumerable<DeviceData> dataList)
    {
        ArgumentNullException.ThrowIfNull(dataList);

        var snapshots = dataList.ToArray();

        try
        {
            if (snapshots.Length == 0)
            {
                return true;
            }

            foreach (var data in snapshots)
            {
                AddToRecentCache(data);
            }

            if (!await PersistBatchAsync(snapshots).ConfigureAwait(false))
            {
                return false;
            }

            foreach (var data in snapshots)
            {
                RaiseDataWritten(data);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to write device data batch: {ex.Message}", ex);

            if (GetPersistenceConfig().FailureStrategy == DataPersistenceFailureStrategy.Throw)
            {
                throw;
            }

            return false;
        }
    }

    /// <inheritdoc />
    public Task<DeviceData?> ReadDataAsync(string deviceId, string pointName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pointName);

        var data = _cache.Get(deviceId);
        if (data != null)
        {
            var point = data.DataItems.FirstOrDefault(p => p.PointName == pointName);
            if (point != null)
            {
                return Task.FromResult<DeviceData?>(data);
            }
        }

        return Task.FromResult<DeviceData?>(null);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<DeviceData>> ReadDataHistoryAsync(string deviceId, DateTime startTime, DateTime endTime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        if (IsPersistenceEnabled())
        {
            return await _persistentStore.ReadAsync(
                deviceId,
                startTime,
                endTime,
                DataCachePurpose.HistoryValue,
                GetPersistenceConfig().MaxHistoryItems).ConfigureAwait(false);
        }

        return ReadRecentValues(deviceId, startTime, endTime);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<DeviceData>> ReadReplayDataAsync(
        string deviceId,
        DateTime startTime,
        DateTime endTime,
        int maxCount = 1000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var effectiveMaxCount = maxCount > 0 ? maxCount : GetPersistenceConfig().ReplayBatchSize;
        if (IsPersistenceEnabled())
        {
            return await _persistentStore.ReadAsync(
                deviceId,
                startTime,
                endTime,
                DataCachePurpose.ReplayValue,
                effectiveMaxCount).ConfigureAwait(false);
        }

        return ReadRecentValues(deviceId, startTime, endTime).Take(effectiveMaxCount);
    }

    /// <inheritdoc />
    public Task<bool> ClearCacheAsync(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        return Task.FromResult(_cache.Remove(deviceId));
    }

    /// <inheritdoc />
    public Task<bool> ClearAllCacheAsync()
    {
        _cache.Clear();
        return Task.FromResult(true);
    }

    private void AddToRecentCache(DeviceData data)
    {
        if (GetGlobalConfig().EnableDataCache)
        {
            _cache.Add(data);
        }
    }

    private async Task<bool> PersistDataAsync(DeviceData data)
    {
        if (!IsPersistenceEnabled())
        {
            return true;
        }

        try
        {
            var accepted = await _persistentStore.WriteAsync(data).ConfigureAwait(false);
            return accepted || HandlePersistenceFailure($"Persistent store rejected data for device '{data.DeviceId}'.", null);
        }
        catch (Exception ex)
        {
            return HandlePersistenceFailure($"Persistent store failed for device '{data.DeviceId}': {ex.Message}", ex);
        }
    }

    private async Task<bool> PersistBatchAsync(IReadOnlyCollection<DeviceData> snapshots)
    {
        if (!IsPersistenceEnabled())
        {
            return true;
        }

        try
        {
            var accepted = await _persistentStore.WriteBatchAsync(snapshots).ConfigureAwait(false);
            return accepted || HandlePersistenceFailure("Persistent store rejected a data batch.", null);
        }
        catch (Exception ex)
        {
            return HandlePersistenceFailure($"Persistent store failed for a data batch: {ex.Message}", ex);
        }
    }

    private bool HandlePersistenceFailure(string message, Exception? exception)
    {
        var strategy = GetPersistenceConfig().FailureStrategy;
        if (strategy == DataPersistenceFailureStrategy.CacheOnly)
        {
            _logger.Warning($"{message} Falling back to recent-value cache only.");
            return true;
        }

        if (strategy == DataPersistenceFailureStrategy.Throw && exception != null)
        {
            throw exception;
        }

        _logger.Error(message, exception);
        return false;
    }

    private IEnumerable<DeviceData> ReadRecentValues(string deviceId, DateTime startTime, DateTime endTime)
    {
        return _cache.GetByDevice(deviceId)
            .Where(d => d.CollectTime >= startTime && d.CollectTime <= endTime)
            .OrderBy(d => d.CollectTime)
            .ToArray();
    }

    private bool IsPersistenceEnabled()
    {
        var config = GetPersistenceConfig();
        return config.Enabled && config.Backend != DataPersistenceBackend.None && _persistentStore.Backend != DataPersistenceBackend.None;
    }

    private DataPersistenceConfig GetPersistenceConfig()
    {
        return _configProvider?.GetConfig().Persistence ?? new DataPersistenceConfig();
    }

    private GlobalConfig GetGlobalConfig()
    {
        return _configProvider?.GetConfig().Global ?? new GlobalConfig();
    }

    private void RaiseDataWritten(DeviceData data)
    {
        DataWritten?.Invoke(this, new DataWrittenEventArgs
        {
            DeviceId = data.DeviceId,
            Data = data,
            Timestamp = DateTime.Now
        });
    }
}
