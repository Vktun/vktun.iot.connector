using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Business.Providers;

/// <summary>
/// Redis-backed recent-value cache keyed by device identifier.
/// </summary>
public sealed class RedisDataCache : IDataCache, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IConnectionMultiplexer _connection;
    private readonly IDatabase _database;
    private readonly string _prefix;
    private readonly bool _ownsConnection;
    private readonly Expiration _keyExpiry;
    private readonly RedisKey _devicesKey;
    private readonly RedisKey _orderKey;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisDataCache"/> class.
    /// </summary>
    /// <param name="connection">The Redis connection multiplexer.</param>
    /// <param name="maxSize">The maximum number of devices retained in the recent-value cache.</param>
    /// <param name="instanceName">The Redis key prefix.</param>
    /// <param name="database">The Redis logical database index.</param>
    /// <param name="keyTtlSeconds">The optional value key TTL in seconds.</param>
    /// <param name="ownsConnection">Whether this cache owns and disposes the Redis connection.</param>
    public RedisDataCache(
        IConnectionMultiplexer connection,
        long maxSize,
        string instanceName,
        int database = -1,
        int keyTtlSeconds = 0,
        bool ownsConnection = false)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        MaxSize = maxSize;
        _prefix = NormalizePrefix(instanceName);
        _database = _connection.GetDatabase(database);
        _ownsConnection = ownsConnection;
        _keyExpiry = keyTtlSeconds > 0 ? TimeSpan.FromSeconds(keyTtlSeconds) : Expiration.Default;
        _devicesKey = BuildKey("devices");
        _orderKey = BuildKey("order");
    }

    /// <inheritdoc />
    public int Count => (int)_database.SetLength(_devicesKey);

    /// <inheritdoc />
    public long MaxSize { get; set; }

    /// <inheritdoc />
    public bool IsFull => Count >= MaxSize;

    /// <summary>
    /// Creates a Redis cache from SDK configuration.
    /// </summary>
    /// <param name="config">The cache configuration.</param>
    /// <returns>A Redis cache when the connection is configured; otherwise <c>null</c>.</returns>
    public static RedisDataCache? TryCreate(CacheConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.Redis.ConnectionString))
        {
            return null;
        }

        var options = ConfigurationOptions.Parse(config.Redis.ConnectionString);
        options.AbortOnConnectFail = config.Redis.AbortOnConnectFail;
        options.ConnectTimeout = config.Redis.ConnectTimeout;
        options.SyncTimeout = config.Redis.SyncTimeout;

        var connection = ConnectionMultiplexer.Connect(options);
        if (!connection.IsConnected)
        {
            connection.Dispose();
            return null;
        }

        return new RedisDataCache(
            connection,
            config.MaxSize,
            config.Redis.InstanceName,
            config.Redis.Database,
            config.Redis.KeyTtlSeconds,
            ownsConnection: true);
    }

    /// <inheritdoc />
    public void Add(DeviceData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var dataKey = BuildDataKey(data.DeviceId);
        var exists = _database.KeyExists(dataKey);
        if (!exists)
        {
            EvictOldestIfNeeded();
        }

        var payload = JsonSerializer.Serialize(data, JsonOptions);
        _database.StringSet(dataKey, payload, _keyExpiry);

        if (!exists)
        {
            _database.SetAdd(_devicesKey, data.DeviceId);
            _database.ListRightPush(_orderKey, data.DeviceId);
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

        var value = _database.StringGet(BuildDataKey(deviceId));
        if (!value.HasValue)
        {
            RemoveIndexes(deviceId);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DeviceData>(value.ToString(), JsonOptions);
        }
        catch (JsonException)
        {
            Remove(deviceId);
            return null;
        }
    }

    /// <inheritdoc />
    public IEnumerable<DeviceData> GetAll()
    {
        return _database.SetMembers(_devicesKey)
            .Select(value => value.ToString())
            .Select(Get)
            .OfType<DeviceData>()
            .ToArray();
    }

    /// <inheritdoc />
    public IEnumerable<DeviceData> GetByDevice(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var data = Get(deviceId);
        return data != null ? new[] { data } : Array.Empty<DeviceData>();
    }

    /// <inheritdoc />
    public bool Remove(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        RemoveIndexes(deviceId);
        return _database.KeyDelete(BuildDataKey(deviceId));
    }

    /// <inheritdoc />
    public void Clear()
    {
        var dataKeys = _database.SetMembers(_devicesKey)
            .Select(value => (RedisKey)BuildDataKey(value.ToString()))
            .Concat(new[] { _devicesKey, _orderKey })
            .ToArray();

        if (dataKeys.Length > 0)
        {
            _database.KeyDelete(dataKeys);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsConnection)
        {
            _connection.Dispose();
        }
    }

    private void EvictOldestIfNeeded()
    {
        while (MaxSize > 0 && Count >= MaxSize)
        {
            var oldestDeviceId = _database.ListLeftPop(_orderKey);
            if (!oldestDeviceId.HasValue)
            {
                break;
            }

            var deviceId = oldestDeviceId.ToString();
            _database.SetRemove(_devicesKey, deviceId);
            _database.KeyDelete(BuildDataKey(deviceId));
        }
    }

    private void RemoveIndexes(string deviceId)
    {
        _database.SetRemove(_devicesKey, deviceId);
        _database.ListRemove(_orderKey, deviceId);
    }

    private RedisKey BuildDataKey(string deviceId)
    {
        return BuildKey($"data:{deviceId}");
    }

    private RedisKey BuildKey(string suffix)
    {
        return $"{_prefix}:{suffix}";
    }

    private static string NormalizePrefix(string instanceName)
    {
        return string.IsNullOrWhiteSpace(instanceName)
            ? "vktun:iot:cache"
            : instanceName.Trim().TrimEnd(':');
    }
}
