using Microsoft.Data.Sqlite;
using Vktun.IoT.Connector.Core.Interfaces;

namespace Vktun.IoT.Connector.DeviceMock.Services.Recording;

public class DataRecordingService
{
    private readonly ILogger _logger;
    private readonly string _databasePath;
    private readonly object _lock = new();
    private bool _isInitialized;
    
    public DataRecordingService(ILogger logger, string? databasePath = null)
    {
        _logger = logger;
        _databasePath = databasePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "recording.db");
        
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
    
    public async Task InitializeAsync()
    {
        lock (_lock)
        {
            if (_isInitialized)
            {
                return;
            }
        }
        
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        
        var createSessionsTable = @"
            CREATE TABLE IF NOT EXISTS Sessions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DeviceId TEXT NOT NULL,
                StartTime TEXT NOT NULL,
                EndTime TEXT,
                Description TEXT
            )";
        
        var createDataPointsTable = @"
            CREATE TABLE IF NOT EXISTS DataPoints (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId INTEGER NOT NULL,
                PointName TEXT NOT NULL,
                Address TEXT NOT NULL,
                DataType TEXT NOT NULL,
                FOREIGN KEY (SessionId) REFERENCES Sessions(Id)
            )";
        
        var createDataRecordsTable = @"
            CREATE TABLE IF NOT EXISTS DataRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DataPointId INTEGER NOT NULL,
                Timestamp TEXT NOT NULL,
                Value TEXT NOT NULL,
                FOREIGN KEY (DataPointId) REFERENCES DataPoints(Id)
            )";
        
        var createEventsTable = @"
            CREATE TABLE IF NOT EXISTS Events (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId INTEGER NOT NULL,
                Timestamp TEXT NOT NULL,
                EventType TEXT NOT NULL,
                Message TEXT NOT NULL,
                FOREIGN KEY (SessionId) REFERENCES Sessions(Id)
            )";
        
        using (var command = new SqliteCommand(createSessionsTable, connection))
        {
            await command.ExecuteNonQueryAsync();
        }
        
        using (var command = new SqliteCommand(createDataPointsTable, connection))
        {
            await command.ExecuteNonQueryAsync();
        }
        
        using (var command = new SqliteCommand(createDataRecordsTable, connection))
        {
            await command.ExecuteNonQueryAsync();
        }
        
        using (var command = new SqliteCommand(createEventsTable, connection))
        {
            await command.ExecuteNonQueryAsync();
        }
        
        lock (_lock)
        {
            _isInitialized = true;
        }
        
        _logger.Info($"数据记录服务已初始化: {_databasePath}");
    }
    
    public async Task<int> CreateSessionAsync(string deviceId, string? description = null)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        
        var sql = "INSERT INTO Sessions (DeviceId, StartTime, Description) VALUES (@deviceId, @startTime, @description); SELECT last_insert_rowid();";
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@deviceId", deviceId);
        command.Parameters.AddWithValue("@startTime", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@description", description ?? string.Empty);
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }
    
    public async Task EndSessionAsync(int sessionId)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        
        var sql = "UPDATE Sessions SET EndTime = @endTime WHERE Id = @id";
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@endTime", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@id", sessionId);
        
        await command.ExecuteNonQueryAsync();
    }
    
    public async Task<int> CreateDataPointAsync(int sessionId, string pointName, string address, string dataType)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        
        var sql = "INSERT INTO DataPoints (SessionId, PointName, Address, DataType) VALUES (@sessionId, @pointName, @address, @dataType); SELECT last_insert_rowid();";
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@sessionId", sessionId);
        command.Parameters.AddWithValue("@pointName", pointName);
        command.Parameters.AddWithValue("@address", address);
        command.Parameters.AddWithValue("@dataType", dataType);
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }
    
    public async Task RecordDataAsync(int dataPointId, object value)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        
        var sql = "INSERT INTO DataRecords (DataPointId, Timestamp, Value) VALUES (@dataPointId, @timestamp, @value)";
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@dataPointId", dataPointId);
        command.Parameters.AddWithValue("@timestamp", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@value", value?.ToString() ?? string.Empty);
        
        await command.ExecuteNonQueryAsync();
    }
    
    public async Task LogEventAsync(int sessionId, string eventType, string message)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        
        var sql = "INSERT INTO Events (SessionId, Timestamp, EventType, Message) VALUES (@sessionId, @timestamp, @eventType, @message)";
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@sessionId", sessionId);
        command.Parameters.AddWithValue("@timestamp", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@eventType", eventType);
        command.Parameters.AddWithValue("@message", message);
        
        await command.ExecuteNonQueryAsync();
    }
    
    public async Task<List<DataRecord>> QueryDataAsync(int sessionId, DateTime? startTime = null, DateTime? endTime = null)
    {
        var records = new List<DataRecord>();
        
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        
        var sql = @"
            SELECT dr.Id, dr.DataPointId, dr.Timestamp, dr.Value, dp.PointName, dp.Address, dp.DataType
            FROM DataRecords dr
            INNER JOIN DataPoints dp ON dr.DataPointId = dp.Id
            INNER JOIN Sessions s ON dp.SessionId = s.Id
            WHERE s.Id = @sessionId";
        
        if (startTime.HasValue)
        {
            sql += " AND dr.Timestamp >= @startTime";
        }
        
        if (endTime.HasValue)
        {
            sql += " AND dr.Timestamp <= @endTime";
        }
        
        sql += " ORDER BY dr.Timestamp";
        
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@sessionId", sessionId);
        
        if (startTime.HasValue)
        {
            command.Parameters.AddWithValue("@startTime", startTime.Value.ToString("O"));
        }
        
        if (endTime.HasValue)
        {
            command.Parameters.AddWithValue("@endTime", endTime.Value.ToString("O"));
        }
        
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(new DataRecord
            {
                Id = reader.GetInt32(0),
                DataPointId = reader.GetInt32(1),
                Timestamp = DateTime.Parse(reader.GetString(2)),
                Value = reader.GetString(3),
                PointName = reader.GetString(4),
                Address = reader.GetString(5),
                DataType = reader.GetString(6)
            });
        }
        
        return records;
    }
}

public class DataRecord
{
    public int Id { get; set; }
    public int DataPointId { get; set; }
    public DateTime Timestamp { get; set; }
    public string Value { get; set; } = string.Empty;
    public string PointName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
}
