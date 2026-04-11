using System.Collections.Concurrent;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Business.Managers;

public class DeviceManager : IDeviceManager
{
    private readonly ConcurrentDictionary<string, DeviceInfo> _devices = new();
    private readonly ConcurrentDictionary<string, DeviceStatus> _deviceStatuses = new();
    private readonly ConcurrentDictionary<string, DeviceStateMachine> _stateMachines = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _reconnectCts = new();
    private readonly ISessionManager _sessionManager;
    private readonly IDeviceCommandExecutor _commandExecutor;
    private readonly ILogger _logger;
    private readonly int _maxReconnectCount;
    private readonly int _reconnectBaseIntervalMs;
    private readonly int _reconnectMaxIntervalMs;

    public event EventHandler<DeviceStatusChangedEventArgs>? DeviceStatusChanged;

    public DeviceManager(
        ISessionManager sessionManager,
        IDeviceCommandExecutor commandExecutor,
        ILogger logger,
        int maxReconnectCount = 100,
        int reconnectBaseIntervalMs = 1000,
        int reconnectMaxIntervalMs = 30000)
    {
        _sessionManager = sessionManager;
        _commandExecutor = commandExecutor;
        _logger = logger;
        _maxReconnectCount = maxReconnectCount;
        _reconnectBaseIntervalMs = reconnectBaseIntervalMs;
        _reconnectMaxIntervalMs = reconnectMaxIntervalMs;
    }

    public Task<bool> AddDeviceAsync(DeviceInfo device)
    {
        if (string.IsNullOrWhiteSpace(device.DeviceId))
        {
            return Task.FromResult(false);
        }

        var added = _devices.TryAdd(device.DeviceId, device);
        if (added)
        {
            _deviceStatuses[device.DeviceId] = DeviceStatus.Offline;
            var stateMachine = new DeviceStateMachine(device.DeviceId, _logger);
            stateMachine.StatusChanged += OnStateMachineStatusChanged;
            _stateMachines[device.DeviceId] = stateMachine;
            _logger.Info($"Device added: {device.DeviceId}");
        }

        return Task.FromResult(added);
    }

    public async Task<bool> RemoveDeviceAsync(string deviceId)
    {
        CancelReconnect(deviceId);

        await DisconnectDeviceAsync(deviceId).ConfigureAwait(false);
        var removed = _devices.TryRemove(deviceId, out _);
        if (removed)
        {
            _deviceStatuses.TryRemove(deviceId, out _);
            if (_stateMachines.TryRemove(deviceId, out var stateMachine))
            {
                stateMachine.StatusChanged -= OnStateMachineStatusChanged;
            }

            await _sessionManager.RemoveSessionAsync(deviceId).ConfigureAwait(false);
            _logger.Info($"Device removed: {deviceId}");
        }

        return removed;
    }

    public Task<DeviceInfo?> GetDeviceAsync(string deviceId)
    {
        _devices.TryGetValue(deviceId, out var device);
        return Task.FromResult(device);
    }

    public Task<IEnumerable<DeviceInfo>> GetAllDevicesAsync()
    {
        return Task.FromResult<IEnumerable<DeviceInfo>>(_devices.Values.ToArray());
    }

    public Task<IEnumerable<DeviceInfo>> GetDevicesByStatusAsync(DeviceStatus status)
    {
        var devices = _devices.Values.Where(device =>
            _deviceStatuses.TryGetValue(device.DeviceId, out var currentStatus) && currentStatus == status);
        return Task.FromResult<IEnumerable<DeviceInfo>>(devices.ToArray());
    }

    public Task<bool> UpdateDeviceStatusAsync(string deviceId, DeviceStatus status)
    {
        if (!_deviceStatuses.TryGetValue(deviceId, out var previousStatus))
        {
            return Task.FromResult(false);
        }

        _deviceStatuses[deviceId] = status;
        if (_devices.TryGetValue(deviceId, out var device))
        {
            device.Status = status;
            if (status == DeviceStatus.Online)
            {
                device.LastConnectTime = DateTime.Now;
                device.ReconnectCount = 0;
            }
        }

        if (previousStatus != status)
        {
            DeviceStatusChanged?.Invoke(this, new DeviceStatusChangedEventArgs
            {
                DeviceId = deviceId,
                OldStatus = previousStatus,
                NewStatus = status,
                Timestamp = DateTime.Now
            });
        }

        return Task.FromResult(true);
    }

    public async Task<bool> ConnectDeviceAsync(string deviceId)
    {
        var device = await GetDeviceAsync(deviceId).ConfigureAwait(false);
        if (device == null)
        {
            return false;
        }

        if (!_stateMachines.TryGetValue(deviceId, out var stateMachine))
        {
            return false;
        }

        if (!stateMachine.TransitionTo(DeviceStatus.Connecting, "User initiated connection"))
        {
            return false;
        }

        var connected = await _commandExecutor.ConnectAsync(device).ConfigureAwait(false);
        if (!connected)
        {
            stateMachine.RecordError(new InvalidOperationException("Connection failed"));
            stateMachine.TransitionTo(DeviceStatus.Error, "Connection failed");
            ScheduleReconnect(deviceId);
            return false;
        }

        await _sessionManager.CreateSessionAsync(device).ConfigureAwait(false);
        stateMachine.TransitionTo(DeviceStatus.Online, "Connected successfully");
        return true;
    }

    public async Task<bool> DisconnectDeviceAsync(string deviceId)
    {
        var device = await GetDeviceAsync(deviceId).ConfigureAwait(false);
        if (device == null)
        {
            return false;
        }

        CancelReconnect(deviceId);

        if (!_stateMachines.TryGetValue(deviceId, out var stateMachine))
        {
            return false;
        }

        if (stateMachine.CurrentState == DeviceStatus.Offline)
        {
            return true;
        }

        stateMachine.TransitionTo(DeviceStatus.Disconnecting, "User initiated disconnection");
        await _commandExecutor.DisconnectAsync(deviceId).ConfigureAwait(false);
        await _sessionManager.RemoveSessionAsync(deviceId).ConfigureAwait(false);
        stateMachine.TransitionTo(DeviceStatus.Offline, "Disconnected");
        return true;
    }

    public async Task<int> ConnectAllAsync()
    {
        var tasks = (await GetAllDevicesAsync().ConfigureAwait(false)).Select(device => ConnectDeviceAsync(device.DeviceId));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.Count(result => result);
    }

    public async Task<int> DisconnectAllAsync()
    {
        var tasks = (await GetAllDevicesAsync().ConfigureAwait(false)).Select(device => DisconnectDeviceAsync(device.DeviceId));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.Count(result => result);
    }

    public void HandleDeviceDisconnected(string deviceId, string reason)
    {
        if (!_stateMachines.TryGetValue(deviceId, out var stateMachine))
        {
            return;
        }

        stateMachine.RecordError(new InvalidOperationException($"Device disconnected: {reason}"));

        if (stateMachine.CurrentState != DeviceStatus.Offline && stateMachine.CurrentState != DeviceStatus.Disconnecting)
        {
            stateMachine.TransitionTo(DeviceStatus.Error, reason);
            ScheduleReconnect(deviceId);
        }
    }

    public DeviceStateMachine? GetStateMachine(string deviceId)
    {
        _stateMachines.TryGetValue(deviceId, out var stateMachine);
        return stateMachine;
    }

    private void OnStateMachineStatusChanged(object? sender, DeviceStatusChangedEventArgs e)
    {
        _deviceStatuses[e.DeviceId] = e.NewStatus;
        if (_devices.TryGetValue(e.DeviceId, out var device))
        {
            device.Status = e.NewStatus;
        }

        DeviceStatusChanged?.Invoke(this, e);
    }

    private void ScheduleReconnect(string deviceId)
    {
        CancelReconnect(deviceId);

        if (!_devices.TryGetValue(deviceId, out var device))
        {
            return;
        }

        if (!_stateMachines.TryGetValue(deviceId, out var stateMachine))
        {
            return;
        }

        var config = new ReconnectContext
        {
            DeviceId = deviceId,
            Attempt = 0,
            MaxAttempts = _maxReconnectCount
        };

        if (!stateMachine.CanRetry(config.MaxAttempts, TimeSpan.FromMinutes(10)))
        {
            _logger.Warning($"Device {deviceId} has exceeded maximum reconnect attempts.");
            return;
        }

        var cts = new CancellationTokenSource();
        _reconnectCts[deviceId] = cts;
        _ = ReconnectLoopAsync(deviceId, config, cts.Token);
    }

    private async Task ReconnectLoopAsync(string deviceId, ReconnectContext context, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && context.Attempt < context.MaxAttempts)
        {
            context.Attempt++;
            var delay = CalculateReconnectDelay(context.Attempt);

            _logger.Info($"Reconnect attempt {context.Attempt}/{context.MaxAttempts} for device {deviceId} in {delay}ms.");

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!_stateMachines.TryGetValue(deviceId, out var stateMachine))
            {
                return;
            }

            if (stateMachine.CurrentState == DeviceStatus.Online || stateMachine.CurrentState == DeviceStatus.Offline)
            {
                return;
            }

            if (!stateMachine.TransitionTo(DeviceStatus.Connecting, $"Reconnect attempt {context.Attempt}"))
            {
                continue;
            }

            var device = await GetDeviceAsync(deviceId).ConfigureAwait(false);
            if (device == null)
            {
                return;
            }

            try
            {
                var connected = await _commandExecutor.ConnectAsync(device, cancellationToken).ConfigureAwait(false);
                if (connected)
                {
                    await _sessionManager.CreateSessionAsync(device).ConfigureAwait(false);
                    stateMachine.TransitionTo(DeviceStatus.Online, $"Reconnected after {context.Attempt} attempts");
                    device.ReconnectCount = context.Attempt;
                    _logger.Info($"Device {deviceId} reconnected after {context.Attempt} attempts.");
                    return;
                }

                stateMachine.RecordError(new InvalidOperationException($"Reconnect attempt {context.Attempt} failed"));
                stateMachine.TransitionTo(DeviceStatus.Error, $"Reconnect attempt {context.Attempt} failed");
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                stateMachine.RecordError(ex);
                stateMachine.TransitionTo(DeviceStatus.Error, $"Reconnect error: {ex.Message}");
            }
        }

        _logger.Warning($"Device {deviceId} exhausted all reconnect attempts.");
    }

    private void CancelReconnect(string deviceId)
    {
        if (_reconnectCts.TryRemove(deviceId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private int CalculateReconnectDelay(int attempt)
    {
        var delay = _reconnectBaseIntervalMs * (1 << Math.Min(attempt - 1, 10));
        delay = Math.Min(delay, _reconnectMaxIntervalMs);
        var jitter = delay * 0.25 * (Random.Shared.NextDouble() * 2 - 1);
        return (int)Math.Max(100, delay + jitter);
    }

    private sealed class ReconnectContext
    {
        public string DeviceId { get; set; } = string.Empty;
        public int Attempt { get; set; }
        public int MaxAttempts { get; set; }
    }
}
