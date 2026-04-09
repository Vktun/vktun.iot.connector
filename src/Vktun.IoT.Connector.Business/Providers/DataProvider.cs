using System.Collections.Concurrent;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Business.Providers;

public class DataCache : IDataCache
{
    private readonly ConcurrentDictionary<string, DeviceData> _cacheByDeviceId;
    private readonly ConcurrentQueue<DeviceData> _orderedCache;
    private readonly object _lockObject = new();

    public int Count => _cacheByDeviceId.Count;
    public long MaxSize { get; set; }
    public bool IsFull => Count >= MaxSize;

    public DataCache(long maxSize = 10000)
    {
        _cacheByDeviceId = new ConcurrentDictionary<string, DeviceData>();
        _orderedCache = new ConcurrentQueue<DeviceData>();
        MaxSize = maxSize;
    }

    public void Add(DeviceData data)
    {
        lock (_lockObject)
        {
            if (IsFull && !_cacheByDeviceId.ContainsKey(data.DeviceId))
            {
                if (_orderedCache.TryDequeue(out var oldest))
                {
                    _cacheByDeviceId.TryRemove(oldest.DeviceId, out _);
                }
            }

            if (_cacheByDeviceId.TryGetValue(data.DeviceId, out var existing))
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

    public void AddRange(IEnumerable<DeviceData> dataList)
    {
        foreach (var data in dataList)
        {
            Add(data);
        }
    }

    public DeviceData? Get(string deviceId)
    {
        _cacheByDeviceId.TryGetValue(deviceId, out var data);
        return data;
    }

    public IEnumerable<DeviceData> GetAll()
    {
        return _cacheByDeviceId.Values.ToArray();
    }

    public IEnumerable<DeviceData> GetByDevice(string deviceId)
    {
        _cacheByDeviceId.TryGetValue(deviceId, out var data);
        return data != null ? new[] { data } : Array.Empty<DeviceData>();
    }

    public bool Remove(string deviceId)
    {
        return _cacheByDeviceId.TryRemove(deviceId, out _);
    }

    public void Clear()
    {
        _cacheByDeviceId.Clear();
        while (_orderedCache.TryDequeue(out _)) { }
    }
}

public class DataProvider : IDataProvider
{
    private readonly IDataCache _cache;
    private readonly ILogger _logger;

    public event EventHandler<DataWrittenEventArgs>? DataWritten;

    public DataProvider(IDataCache cache, ILogger logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task<bool> WriteDataAsync(DeviceData data)
    {
        try
        {
            _cache.Add(data);
            DataWritten?.Invoke(this, new DataWrittenEventArgs
            {
                DeviceId = data.DeviceId,
                Data = data,
                Timestamp = DateTime.Now
            });
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.Error($"写入数据失败: {ex.Message}", ex);
            return Task.FromResult(false);
        }
    }

    public Task<bool> WriteDataBatchAsync(IEnumerable<DeviceData> dataList)
    {
        try
        {
            _cache.AddRange(dataList);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.Error($"批量写入数据失败: {ex.Message}", ex);
            return Task.FromResult(false);
        }
    }

    public Task<DeviceData?> ReadDataAsync(string deviceId, string pointName)
    {
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

    public Task<IEnumerable<DeviceData>> ReadDataHistoryAsync(string deviceId, DateTime startTime, DateTime endTime)
    {
        var data = _cache.GetByDevice(deviceId)
            .Where(d => d.CollectTime >= startTime && d.CollectTime <= endTime);
        return Task.FromResult(data);
    }

    public Task<bool> ClearCacheAsync(string deviceId)
    {
        return Task.FromResult(_cache.Remove(deviceId));
    }

    public Task<bool> ClearAllCacheAsync()
    {
        _cache.Clear();
        return Task.FromResult(true);
    }
}
