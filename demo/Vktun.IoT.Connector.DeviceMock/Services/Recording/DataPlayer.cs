using Microsoft.Data.Sqlite;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.DeviceMock.Services.Recording;

namespace Vktun.IoT.Connector.DeviceMock.Services;

public class DataPlayer
{
    private readonly ILogger _logger;
    private readonly DataRecordingService _recordingService;
    private System.Threading.Timer? _playbackTimer;
    private List<DataRecord> _records = new();
    private int _currentIndex;
    private double _playbackSpeed = 1.0;
    private bool _isPlaying;
    private bool _isPaused;
    
    public bool IsPlaying => _isPlaying;
    public bool IsPaused => _isPaused;
    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set => _playbackSpeed = Math.Max(0.1, Math.Min(10.0, value));
    }
    
    public event EventHandler<DataPlaybackEventArgs>? DataPointPlayed;
    public event EventHandler? PlaybackStarted;
    public event EventHandler? PlaybackStopped;
    public event EventHandler? PlaybackCompleted;
    
    public DataPlayer(ILogger logger, DataRecordingService recordingService)
    {
        _logger = logger;
        _recordingService = recordingService;
    }
    
    public async Task LoadSessionAsync(int sessionId, DateTime? startTime = null, DateTime? endTime = null)
    {
        try
        {
            _records = await _recordingService.QueryDataAsync(sessionId, startTime, endTime);
            _currentIndex = 0;
            
            _logger.Info($"加载会话 {sessionId} 的数据，共 {_records.Count} 条记录");
        }
        catch (Exception ex)
        {
            _logger.Error($"加载会话数据失败: {ex.Message}", ex);
        }
    }
    
    public void StartPlayback(Action<string, object>? onDataPoint = null)
    {
        if (_isPlaying && !_isPaused)
        {
            return;
        }
        
        if (_records.Count == 0)
        {
            _logger.Warning("没有可回放的数据");
            return;
        }
        
        _isPlaying = true;
        _isPaused = false;
        
        PlaybackStarted?.Invoke(this, EventArgs.Empty);
        _logger.Info("开始回放数据");
        
        _playbackTimer = new Timer(_ =>
        {
            if (!_isPlaying || _isPaused)
            {
                return;
            }
            
            if (_currentIndex >= _records.Count)
            {
                StopPlayback();
                PlaybackCompleted?.Invoke(this, EventArgs.Empty);
                _logger.Info("数据回放完成");
                return;
            }
            
            var record = _records[_currentIndex];
            
            DataPointPlayed?.Invoke(this, new DataPlaybackEventArgs
            {
                Record = record,
                Progress = (double)(_currentIndex + 1) / _records.Count * 100
            });
            
            onDataPoint?.Invoke(record.Address, ParseValue(record.Value, record.DataType));
            
            _currentIndex++;
            
            if (_currentIndex < _records.Count)
            {
                var currentTimestamp = _records[_currentIndex - 1].Timestamp;
                var nextTimestamp = _records[_currentIndex].Timestamp;
                var delay = (nextTimestamp - currentTimestamp).TotalMilliseconds / _playbackSpeed;
                
                _playbackTimer?.Change(TimeSpan.FromMilliseconds(Math.Max(10, delay)), TimeSpan.FromMilliseconds(-1));
            }
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(-1));
    }
    
    public void PausePlayback()
    {
        if (!_isPlaying)
        {
            return;
        }
        
        _isPaused = true;
        _logger.Info("暂停数据回放");
    }
    
    public void ResumePlayback()
    {
        if (!_isPlaying || !_isPaused)
        {
            return;
        }
        
        _isPaused = false;
        _logger.Info("恢复数据回放");
    }
    
    public void StopPlayback()
    {
        _isPlaying = false;
        _isPaused = false;
        
        _playbackTimer?.Dispose();
        _playbackTimer = null;
        
        PlaybackStopped?.Invoke(this, EventArgs.Empty);
        _logger.Info("停止数据回放");
    }
    
    public void SeekTo(double progress)
    {
        if (!_isPlaying || _records.Count == 0)
        {
            return;
        }
        
        _currentIndex = (int)(progress / 100 * _records.Count);
        _currentIndex = Math.Max(0, Math.Min(_currentIndex, _records.Count - 1));
        
        _logger.Info($"跳转到 {progress:F1}% 位置");
    }
    
    public void SeekTo(DateTime timestamp)
    {
        if (!_isPlaying || _records.Count == 0)
        {
            return;
        }
        
        var index = _records.FindIndex(r => r.Timestamp >= timestamp);
        if (index >= 0)
        {
            _currentIndex = index;
            _logger.Info($"跳转到时间点 {timestamp}");
        }
    }
    
    public PlaybackStatus GetStatus()
    {
        return new PlaybackStatus
        {
            IsPlaying = _isPlaying,
            IsPaused = _isPaused,
            CurrentIndex = _currentIndex,
            TotalRecords = _records.Count,
            Progress = _records.Count > 0 ? (double)_currentIndex / _records.Count * 100 : 0,
            PlaybackSpeed = _playbackSpeed
        };
    }
    
    private object ParseValue(string value, string dataType)
    {
        return dataType.ToLower() switch
        {
            "boolean" or "bool" => bool.Parse(value),
            "byte" or "uint8" => byte.Parse(value),
            "sbyte" or "int8" => sbyte.Parse(value),
            "uint16" => ushort.Parse(value),
            "int16" => short.Parse(value),
            "uint32" => uint.Parse(value),
            "int32" => int.Parse(value),
            "uint64" => ulong.Parse(value),
            "int64" => long.Parse(value),
            "float" or "single" => float.Parse(value),
            "double" => double.Parse(value),
            _ => value
        };
    }
}

public class DataPlaybackEventArgs : EventArgs
{
    public DataRecord Record { get; set; } = new();
    public double Progress { get; set; }
}

public class PlaybackStatus
{
    public bool IsPlaying { get; set; }
    public bool IsPaused { get; set; }
    public int CurrentIndex { get; set; }
    public int TotalRecords { get; set; }
    public double Progress { get; set; }
    public double PlaybackSpeed { get; set; }
}
