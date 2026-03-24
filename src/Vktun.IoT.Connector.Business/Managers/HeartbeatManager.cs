using System.Collections.Concurrent;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Business.Managers;

public class HeartbeatManager : IHeartbeatManager
{
    private readonly ConcurrentDictionary<string, HeartbeatInfo> _deviceHeartbeats;
    private readonly IConfigurationProvider _configProvider;
    private readonly ILogger _logger;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _heartbeatTask;
    private bool _isRunning;

    public int Interval { get; set; } = 15000;
    public int Timeout { get; set; } = 30000;
    public bool IsRunning => _isRunning;

    public event EventHandler<HeartbeatMissedEventArgs>? HeartbeatMissed;
    public event EventHandler<HeartbeatReceivedEventArgs>? HeartbeatReceived;

    public HeartbeatManager(IConfigurationProvider configProvider, ILogger logger)
    {
        _deviceHeartbeats = new ConcurrentDictionary<string, HeartbeatInfo>();
        _configProvider = configProvider;
        _logger = logger;
        
        var config = configProvider.GetConfig();
        Interval = config.Tcp.HeartbeatInterval;
        Timeout = config.Tcp.HeartbeatTimeout;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return Task.CompletedTask;
        }

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isRunning = true;
        _heartbeatTask = HeartbeatLoopAsync(_cancellationTokenSource.Token);
        
        _logger.Info("心跳管理器启动");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _cancellationTokenSource?.Cancel();
        
        if (_heartbeatTask != null)
        {
            await _heartbeatTask;
        }
        
        _cancellationTokenSource?.Dispose();
        _logger.Info("心跳管理器停止");
    }

    public Task<bool> SendHeartbeatAsync(string deviceId)
    {
        if (_deviceHeartbeats.TryGetValue(deviceId, out var info))
        {
            info.LastHeartbeatTime = DateTime.Now;
            info.MissedCount = 0;
            
            HeartbeatReceived?.Invoke(this, new HeartbeatReceivedEventArgs
            {
                DeviceId = deviceId,
                Timestamp = DateTime.Now
            });
            
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public void RegisterDevice(string deviceId, int interval = 0)
    {
        var heartbeatInterval = interval > 0 ? interval : Interval;
        _deviceHeartbeats[deviceId] = new HeartbeatInfo
        {
            DeviceId = deviceId,
            Interval = heartbeatInterval,
            LastHeartbeatTime = DateTime.Now,
            MissedCount = 0
        };
    }

    public void UnregisterDevice(string deviceId)
    {
        _deviceHeartbeats.TryRemove(deviceId, out _);
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, cancellationToken);
                CheckHeartbeats();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error($"心跳检查异常: {ex.Message}", ex);
            }
        }
    }

    private void CheckHeartbeats()
    {
        var now = DateTime.Now;
        foreach (var kvp in _deviceHeartbeats)
        {
            var info = kvp.Value;
            var elapsed = (now - info.LastHeartbeatTime).TotalMilliseconds;
            
            if (elapsed > Timeout)
            {
                info.MissedCount++;
                
                HeartbeatMissed?.Invoke(this, new HeartbeatMissedEventArgs
                {
                    DeviceId = info.DeviceId,
                    MissedCount = info.MissedCount,
                    LastHeartbeatTime = info.LastHeartbeatTime,
                    Timestamp = now
                });
                
                _logger.Warning($"设备心跳超时: {info.DeviceId}, 丢失次数: {info.MissedCount}");
            }
        }
    }

    private class HeartbeatInfo
    {
        public string DeviceId { get; set; } = string.Empty;
        public int Interval { get; set; }
        public DateTime LastHeartbeatTime { get; set; }
        public int MissedCount { get; set; }
    }
}
