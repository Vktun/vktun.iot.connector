using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;

namespace Vktun.IoT.Connector.Core.Models;

/// <summary>
/// 设备状态机 - 管理设备生命周期状态转换
/// </summary>
public class DeviceStateMachine
{
    private readonly string _deviceId;
    private readonly ILogger _logger;
    private DeviceStatus _currentState;
    private readonly object _lock = new();
    private int _errorCount;
    private DateTime? _lastErrorTime;
    private DateTime _stateChangedAt;

    public event EventHandler<DeviceStatusChangedEventArgs>? StatusChanged;

    public DeviceStateMachine(string deviceId, ILogger logger)
    {
        _deviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _currentState = DeviceStatus.Offline;
        _stateChangedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 当前状态
    /// </summary>
    public DeviceStatus CurrentState
    {
        get
        {
            lock (_lock)
            {
                return _currentState;
            }
        }
    }

    /// <summary>
    /// 当前状态持续时间
    /// </summary>
    public TimeSpan CurrentStateDuration
    {
        get
        {
            lock (_lock)
            {
                return DateTime.UtcNow - _stateChangedAt;
            }
        }
    }

    /// <summary>
    /// 错误计数
    /// </summary>
    public int ErrorCount
    {
        get
        {
            lock (_lock)
            {
                return _errorCount;
            }
        }
    }

    /// <summary>
    /// 尝试转换到目标状态
    /// </summary>
    public bool TransitionTo(DeviceStatus targetState, string? reason = null)
    {
        lock (_lock)
        {
            // 验证状态转换是否合法
            if (!IsValidTransition(_currentState, targetState))
            {
                _logger.Warning(
                    $"Invalid state transition for device {_deviceId}: {_currentState} -> {targetState}");
                return false;
            }

            var previousState = _currentState;
            _currentState = targetState;
            _stateChangedAt = DateTime.UtcNow;

            // 重置错误计数（如果进入Online状态）
            if (targetState == DeviceStatus.Online)
            {
                _errorCount = 0;
                _lastErrorTime = null;
            }

            _logger.Info(
                $"Device {_deviceId} state changed: {previousState} -> {targetState}{(reason != null ? $". Reason: {reason}" : "")}");

            // 触发事件
            StatusChanged?.Invoke(this, new DeviceStatusChangedEventArgs
            {
                DeviceId = _deviceId,
                OldStatus = previousState,
                NewStatus = targetState,
                Timestamp = DateTime.UtcNow,
                Message = reason
            });

            return true;
        }
    }

    /// <summary>
    /// 记录错误
    /// </summary>
    public void RecordError(Exception exception)
    {
        lock (_lock)
        {
            _errorCount++;
            _lastErrorTime = DateTime.UtcNow;

            _logger.Error(
                $"Device {_deviceId} error (count: {_errorCount}): {exception.Message}",
                exception);

            // 如果错误次数过多，自动切换到Error状态
            if (_errorCount >= 5 && _currentState != DeviceStatus.Error)
            {
                TransitionTo(DeviceStatus.Error, $"Too many errors ({_errorCount})");
            }
        }
    }

    /// <summary>
    /// 重置错误计数
    /// </summary>
    public void ResetErrors()
    {
        lock (_lock)
        {
            _errorCount = 0;
            _lastErrorTime = null;
            _logger.Debug($"Device {_deviceId} error count reset");
        }
    }

    /// <summary>
    /// 是否可以重试连接
    /// </summary>
    public bool CanRetry(int maxRetries, TimeSpan retryWindow)
    {
        lock (_lock)
        {
            if (_errorCount >= maxRetries)
                return false;

            if (_lastErrorTime.HasValue && DateTime.UtcNow - _lastErrorTime.Value > retryWindow)
            {
                // 错误窗口已过，重置计数
                _errorCount = 0;
                return true;
            }

            return true;
        }
    }

    /// <summary>
    /// 获取状态历史摘要
    /// </summary>
    public string GetStatusSummary()
    {
        lock (_lock)
        {
            return $"Device: {_deviceId}, State: {_currentState}, Duration: {CurrentStateDuration}, Errors: {_errorCount}";
        }
    }

    /// <summary>
    /// 验证状态转换是否合法
    /// </summary>
    private static bool IsValidTransition(DeviceStatus from, DeviceStatus to)
    {
        // 定义合法的状态转换
        var validTransitions = new Dictionary<DeviceStatus, HashSet<DeviceStatus>>
        {
            { DeviceStatus.Offline, new HashSet<DeviceStatus> { DeviceStatus.Connecting, DeviceStatus.Error } },
            { DeviceStatus.Connecting, new HashSet<DeviceStatus> { DeviceStatus.Online, DeviceStatus.Error, DeviceStatus.Offline } },
            { DeviceStatus.Online, new HashSet<DeviceStatus> { DeviceStatus.Disconnecting, DeviceStatus.Error } },
            { DeviceStatus.Disconnecting, new HashSet<DeviceStatus> { DeviceStatus.Offline, DeviceStatus.Error } },
            { DeviceStatus.Error, new HashSet<DeviceStatus> { DeviceStatus.Offline, DeviceStatus.Connecting } }
        };

        return validTransitions.TryGetValue(from, out var targets) && targets.Contains(to);
    }
}
