using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.DeviceMock.Models;
using Vktun.IoT.Connector.DeviceMock.Services.Recording;

namespace Vktun.IoT.Connector.DeviceMock.Services;

public class DataRecorder
{
    private readonly ILogger _logger;
    private readonly DataRecordingService _recordingService;
    private readonly Dictionary<string, int> _dataPointIds = new();
    private int _currentSessionId;
    private bool _isRecording;
    private System.Threading.Timer? _recordingTimer;
    
    public bool IsRecording => _isRecording;
    public int CurrentSessionId => _currentSessionId;
    
    public DataRecorder(ILogger logger, DataRecordingService recordingService)
    {
        _logger = logger;
        _recordingService = recordingService;
    }
    
    public async Task StartRecordingAsync(string deviceId, List<MockDataPoint> dataPoints, string? description = null)
    {
        if (_isRecording)
        {
            _logger.Warning("已经在记录中，请先停止当前记录");
            return;
        }
        
        try
        {
            await _recordingService.InitializeAsync();
            
            _currentSessionId = await _recordingService.CreateSessionAsync(deviceId, description);
            _dataPointIds.Clear();
            
            foreach (var dataPoint in dataPoints)
            {
                var dataPointId = await _recordingService.CreateDataPointAsync(
                    _currentSessionId,
                    dataPoint.PointName,
                    dataPoint.Address,
                    dataPoint.DataType.ToString());
                
                _dataPointIds[dataPoint.Address] = dataPointId;
            }
            
            _isRecording = true;
            _logger.Info($"开始记录数据，会话ID: {_currentSessionId}");
            
            await _recordingService.LogEventAsync(_currentSessionId, "RecordingStarted", $"开始记录设备 {deviceId} 的数据");
        }
        catch (Exception ex)
        {
            _logger.Error($"启动数据记录失败: {ex.Message}", ex);
        }
    }
    
    public async Task StopRecordingAsync()
    {
        if (!_isRecording)
        {
            return;
        }
        
        try
        {
            _recordingTimer?.Dispose();
            _recordingTimer = null;
            
            await _recordingService.EndSessionAsync(_currentSessionId);
            await _recordingService.LogEventAsync(_currentSessionId, "RecordingStopped", "停止记录数据");
            
            _isRecording = false;
            _logger.Info($"停止记录数据，会话ID: {_currentSessionId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"停止数据记录失败: {ex.Message}", ex);
        }
    }
    
    public async Task RecordValueAsync(string address, object value)
    {
        if (!_isRecording || !_dataPointIds.TryGetValue(address, out var dataPointId))
        {
            return;
        }
        
        try
        {
            await _recordingService.RecordDataAsync(dataPointId, value);
        }
        catch (Exception ex)
        {
            _logger.Error($"记录数据失败: {ex.Message}", ex);
        }
    }
    
    public void StartAutoRecording(IDeviceSimulator simulator, List<MockDataPoint> dataPoints, int intervalMs = 1000)
    {
        if (_isRecording)
        {
            return;
        }
        
        _ = StartRecordingAsync(simulator.DeviceId, dataPoints, $"自动记录 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        
        _recordingTimer = new Timer(async _ =>
        {
            if (!_isRecording)
            {
                return;
            }
            
            foreach (var dataPoint in dataPoints)
            {
                try
                {
                    var value = simulator.GetDataPoint(dataPoint.Address);
                    await RecordValueAsync(dataPoint.Address, value);
                }
                catch (Exception ex)
                {
                    _logger.Error($"自动记录数据点 {dataPoint.Address} 失败: {ex.Message}", ex);
                }
            }
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(intervalMs));
    }
    
    public void StopAutoRecording()
    {
        _recordingTimer?.Dispose();
        _recordingTimer = null;
        _ = StopRecordingAsync();
    }
}
