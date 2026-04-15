using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Business.Providers;

/// <summary>
/// Persistent data store that intentionally discards all writes.
/// </summary>
public sealed class NoopDataPersistenceStore : IPersistentDataStore
{
    /// <inheritdoc />
    public DataPersistenceBackend Backend => DataPersistenceBackend.None;

    /// <inheritdoc />
    public Task<bool> WriteAsync(DeviceData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> WriteBatchAsync(IEnumerable<DeviceData> dataList, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataList);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DeviceData>> ReadAsync(
        string deviceId,
        DateTime startTime,
        DateTime endTime,
        DataCachePurpose purpose,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        return Task.FromResult<IReadOnlyList<DeviceData>>(Array.Empty<DeviceData>());
    }
}

/// <summary>
/// In-memory persistent store used for historical and replay values.
/// </summary>
public sealed class MemoryDataPersistenceStore : IPersistentDataStore
{
    private readonly ConcurrentQueue<DeviceData> _data = new();
    private readonly object _trimLock = new();
    private readonly int _maxHistoryItems;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryDataPersistenceStore"/> class.
    /// </summary>
    /// <param name="maxHistoryItems">The maximum number of historical snapshots retained in memory.</param>
    public MemoryDataPersistenceStore(int maxHistoryItems = 100000)
    {
        _maxHistoryItems = Math.Max(1, maxHistoryItems);
    }

    /// <inheritdoc />
    public DataPersistenceBackend Backend => DataPersistenceBackend.Memory;

    /// <inheritdoc />
    public Task<bool> WriteAsync(DeviceData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        cancellationToken.ThrowIfCancellationRequested();

        _data.Enqueue(data);
        TrimIfNeeded();
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async Task<bool> WriteBatchAsync(IEnumerable<DeviceData> dataList, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataList);

        foreach (var data in dataList)
        {
            await WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DeviceData>> ReadAsync(
        string deviceId,
        DateTime startTime,
        DateTime endTime,
        DataCachePurpose purpose,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        cancellationToken.ThrowIfCancellationRequested();

        var limit = NormalizeMaxCount(maxCount);
        var result = _data
            .Where(d => d.DeviceId == deviceId && d.CollectTime >= startTime && d.CollectTime <= endTime)
            .OrderBy(d => d.CollectTime)
            .Take(limit)
            .ToArray();

        return Task.FromResult<IReadOnlyList<DeviceData>>(result);
    }

    private void TrimIfNeeded()
    {
        if (_data.Count <= _maxHistoryItems)
        {
            return;
        }

        lock (_trimLock)
        {
            while (_data.Count > _maxHistoryItems && _data.TryDequeue(out _))
            {
            }
        }
    }

    private int NormalizeMaxCount(int maxCount)
    {
        return maxCount > 0 ? maxCount : _maxHistoryItems;
    }
}

/// <summary>
/// JSON Lines file-backed persistent store.
/// </summary>
public sealed class FileDataPersistenceStore : IPersistentDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="FileDataPersistenceStore"/> class.
    /// </summary>
    /// <param name="filePath">The JSON Lines file path.</param>
    public FileDataPersistenceStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        var directory = Path.GetDirectoryName(Path.GetFullPath(_filePath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <inheritdoc />
    public DataPersistenceBackend Backend => DataPersistenceBackend.File;

    /// <inheritdoc />
    public async Task<bool> WriteAsync(DeviceData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        var json = JsonSerializer.Serialize(data, JsonOptions);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(_filePath, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> WriteBatchAsync(IEnumerable<DeviceData> dataList, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataList);

        var lines = dataList.Select(d => JsonSerializer.Serialize(d, JsonOptions)).ToArray();
        if (lines.Length == 0)
        {
            return true;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllLinesAsync(_filePath, lines, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceData>> ReadAsync(
        string deviceId,
        DateTime startTime,
        DateTime endTime,
        DataCachePurpose purpose,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        if (!File.Exists(_filePath))
        {
            return Array.Empty<DeviceData>();
        }

        var limit = NormalizeMaxCount(maxCount);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var lines = await File.ReadAllLinesAsync(_filePath, cancellationToken).ConfigureAwait(false);
            return lines
                .Select(TryDeserialize)
                .OfType<DeviceData>()
                .Where(d => d.DeviceId == deviceId && d.CollectTime >= startTime && d.CollectTime <= endTime)
                .OrderBy(d => d.CollectTime)
                .Take(limit)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static DeviceData? TryDeserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<DeviceData>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int NormalizeMaxCount(int maxCount)
    {
        return maxCount > 0 ? maxCount : int.MaxValue;
    }
}

/// <summary>
/// SQLite-backed persistent store for historical and replay values.
/// </summary>
public sealed class SqliteDataPersistenceStore : IPersistentDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteDataPersistenceStore"/> class.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string.</param>
    public SqliteDataPersistenceStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
        InitializeDatabase();
    }

    /// <inheritdoc />
    public DataPersistenceBackend Backend => DataPersistenceBackend.Sqlite;

    /// <inheritdoc />
    public async Task<bool> WriteAsync(DeviceData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await InsertAsync(connection, null, data, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> WriteBatchAsync(IEnumerable<DeviceData> dataList, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataList);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var data in dataList)
        {
            await InsertAsync(connection, transaction, data, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceData>> ReadAsync(
        string deviceId,
        DateTime startTime,
        DateTime endTime,
        DataCachePurpose purpose,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var result = new List<DeviceData>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload
            FROM device_data
            WHERE device_id = $deviceId
              AND collect_time_ticks >= $startTicks
              AND collect_time_ticks <= $endTicks
            ORDER BY collect_time_ticks ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$deviceId", deviceId);
        command.Parameters.AddWithValue("$startTicks", startTime.Ticks);
        command.Parameters.AddWithValue("$endTicks", endTime.Ticks);
        command.Parameters.AddWithValue("$limit", NormalizeMaxCount(maxCount));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var json = reader.GetString(0);
            var data = JsonSerializer.Deserialize<DeviceData>(json, JsonOptions);
            if (data != null)
            {
                result.Add(data);
            }
        }

        return result;
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS device_data (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                device_id TEXT NOT NULL,
                channel_id TEXT NOT NULL,
                collect_time_ticks INTEGER NOT NULL,
                payload TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_device_data_device_time
            ON device_data (device_id, collect_time_ticks);
            """;
        command.ExecuteNonQuery();
    }

    private static async Task InsertAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction? transaction,
        DeviceData data,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction?)transaction;
        command.CommandText = """
            INSERT INTO device_data (device_id, channel_id, collect_time_ticks, payload)
            VALUES ($deviceId, $channelId, $collectTimeTicks, $payload);
            """;
        command.Parameters.AddWithValue("$deviceId", data.DeviceId);
        command.Parameters.AddWithValue("$channelId", data.ChannelId);
        command.Parameters.AddWithValue("$collectTimeTicks", data.CollectTime.Ticks);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(data, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static int NormalizeMaxCount(int maxCount)
    {
        return maxCount > 0 ? maxCount : int.MaxValue;
    }
}

/// <summary>
/// Adapter for externally managed persistent stores such as relational databases or message queues.
/// </summary>
public sealed class DelegateDataPersistenceStore : IPersistentDataStore
{
    private readonly Func<DeviceData, CancellationToken, Task<bool>> _writeAsync;
    private readonly Func<IEnumerable<DeviceData>, CancellationToken, Task<bool>>? _writeBatchAsync;
    private readonly Func<string, DateTime, DateTime, DataCachePurpose, int, CancellationToken, Task<IReadOnlyList<DeviceData>>> _readAsync;

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegateDataPersistenceStore"/> class.
    /// </summary>
    /// <param name="writeAsync">The delegate that writes one data snapshot.</param>
    /// <param name="readAsync">The delegate that reads historical or replay snapshots.</param>
    /// <param name="writeBatchAsync">The optional delegate that writes a data snapshot batch.</param>
    public DelegateDataPersistenceStore(
        Func<DeviceData, CancellationToken, Task<bool>> writeAsync,
        Func<string, DateTime, DateTime, DataCachePurpose, int, CancellationToken, Task<IReadOnlyList<DeviceData>>> readAsync,
        Func<IEnumerable<DeviceData>, CancellationToken, Task<bool>>? writeBatchAsync = null)
    {
        _writeAsync = writeAsync ?? throw new ArgumentNullException(nameof(writeAsync));
        _readAsync = readAsync ?? throw new ArgumentNullException(nameof(readAsync));
        _writeBatchAsync = writeBatchAsync;
    }

    /// <inheritdoc />
    public DataPersistenceBackend Backend => DataPersistenceBackend.External;

    /// <inheritdoc />
    public Task<bool> WriteAsync(DeviceData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return _writeAsync(data, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> WriteBatchAsync(IEnumerable<DeviceData> dataList, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataList);

        if (_writeBatchAsync != null)
        {
            return await _writeBatchAsync(dataList, cancellationToken).ConfigureAwait(false);
        }

        foreach (var data in dataList)
        {
            if (!await WriteAsync(data, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DeviceData>> ReadAsync(
        string deviceId,
        DateTime startTime,
        DateTime endTime,
        DataCachePurpose purpose,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        return _readAsync(deviceId, startTime, endTime, purpose, maxCount, cancellationToken);
    }
}

/// <summary>
/// Bounded write-buffer wrapper that applies persistent-storage backpressure.
/// </summary>
public sealed class BufferedDataPersistenceStore : IPersistentDataStore, IAsyncDisposable, IDisposable
{
    private readonly IPersistentDataStore _innerStore;
    private readonly DataPersistenceConfig _config;
    private readonly ILogger _logger;
    private readonly Channel<DeviceData>? _writeChannel;
    private readonly CancellationTokenSource _stopSignal = new();
    private readonly Task? _worker;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferedDataPersistenceStore"/> class.
    /// </summary>
    /// <param name="innerStore">The wrapped persistent store.</param>
    /// <param name="config">The persistence configuration.</param>
    /// <param name="logger">The logger.</param>
    public BufferedDataPersistenceStore(IPersistentDataStore innerStore, DataPersistenceConfig config, ILogger logger)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_config.Enabled && _config.EnableWriteBuffer && _innerStore.Backend != DataPersistenceBackend.None)
        {
            _writeChannel = Channel.CreateBounded<DeviceData>(new BoundedChannelOptions(Math.Max(1, _config.WriteQueueCapacity))
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = ToChannelFullMode(_config.BackpressureStrategy)
            });
            _worker = Task.Run(ProcessWritesAsync);
        }
    }

    /// <inheritdoc />
    public DataPersistenceBackend Backend => _innerStore.Backend;

    /// <inheritdoc />
    public async Task<bool> WriteAsync(DeviceData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (_writeChannel == null)
        {
            return await _innerStore.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }

        if (_config.BackpressureStrategy == DataPersistenceBackpressureStrategy.Wait)
        {
            await _writeChannel.Writer.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            return true;
        }

        return _writeChannel.Writer.TryWrite(data);
    }

    /// <inheritdoc />
    public async Task<bool> WriteBatchAsync(IEnumerable<DeviceData> dataList, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataList);

        foreach (var data in dataList)
        {
            if (!await WriteAsync(data, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DeviceData>> ReadAsync(
        string deviceId,
        DateTime startTime,
        DateTime endTime,
        DataCachePurpose purpose,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        return _innerStore.ReadAsync(deviceId, startTime, endTime, purpose, maxCount, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writeChannel?.Writer.TryComplete();

        if (_worker != null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _stopSignal.Cancel();
        _stopSignal.Dispose();
    }

    private async Task ProcessWritesAsync()
    {
        if (_writeChannel == null)
        {
            return;
        }

        await foreach (var data in _writeChannel.Reader.ReadAllAsync(_stopSignal.Token).ConfigureAwait(false))
        {
            try
            {
                if (!await _innerStore.WriteAsync(data, _stopSignal.Token).ConfigureAwait(false))
                {
                    _logger.Warning($"Persistent store rejected buffered data for device '{data.DeviceId}'.");
                }
            }
            catch (OperationCanceledException) when (_stopSignal.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error($"Buffered persistent write failed for device '{data.DeviceId}': {ex.Message}", ex);
            }
        }
    }

    private static BoundedChannelFullMode ToChannelFullMode(DataPersistenceBackpressureStrategy strategy)
    {
        return strategy switch
        {
            DataPersistenceBackpressureStrategy.DropOldest => BoundedChannelFullMode.DropOldest,
            DataPersistenceBackpressureStrategy.DropNewest => BoundedChannelFullMode.DropNewest,
            _ => BoundedChannelFullMode.Wait
        };
    }
}
