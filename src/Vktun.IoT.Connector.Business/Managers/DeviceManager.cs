using System.Collections.Concurrent;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Business.Services;

namespace Vktun.IoT.Connector.Business.Managers;

public class DeviceManager : IDeviceManager
{
    private readonly ConcurrentDictionary<string, DeviceInfo> _devices = new();
    private readonly ConcurrentDictionary<string, DeviceStatus> _deviceStatuses = new();
    private readonly ConcurrentDictionary<string, DeviceStateMachine> _stateMachines = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _reconnectCts = new();
    private readonly ConcurrentDictionary<string, IReconnectPolicy> _deviceReconnectPolicies = new();
    private readonly ISessionManager _sessionManager;
    private readonly IDeviceCommandExecutor _commandExecutor;
    private readonly ILogger _logger;
    private readonly IResourceMonitor? _resourceMonitor;
    private readonly IReconnectPolicy _defaultReconnectPolicy;

    public event EventHandler<DeviceStatusChangedEventArgs>? DeviceStatusChanged;

    public DeviceManager(
        ISessionManager sessionManager,
        IDeviceCommandExecutor commandExecutor,
        ILogger logger,
        int maxReconnectCount = 100,
        int reconnectBaseIntervalMs = 1000,
        int reconnectMaxIntervalMs = 30000,
        IResourceMonitor? resourceMonitor = null)
        : this(sessionManager, commandExecutor, logger,
            ExponentialBackoffReconnectPolicy.FromGlobalConfig(maxReconnectCount, reconnectBaseIntervalMs, reconnectMaxIntervalMs),
            resourceMonitor)
    {
    }

    public DeviceManager(
        ISessionManager sessionManager,
        IDeviceCommandExecutor commandExecutor,
        ILogger logger,
        IReconnectPolicy reconnectPolicy,
        IResourceMonitor? resourceMonitor = null)
    {
        _sessionManager = sessionManager;
        _commandExecutor = commandExecutor;
        _logger = logger;
        _resourceMonitor = resourceMonitor;
        _defaultReconnectPolicy = reconnectPolicy ?? ExponentialBackoffReconnectPolicy.CreateDefault();
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

            _deviceReconnectPolicies.TryRemove(deviceId, out _);
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

    public void SetReconnectPolicy(string deviceId, IReconnectPolicy policy)
    {
        _deviceReconnectPolicies[deviceId] = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public IReconnectPolicy GetReconnectPolicy(string deviceId)
    {
        return _deviceReconnectPolicies.TryGetValue(deviceId, out var policy)
            ? policy
            : _defaultReconnectPolicy;
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

        var policy = GetReconnectPolicy(deviceId);
        var firstDelay = policy.GetNextDelay(1, null);
        if (firstDelay == null)
        {
            _logger.Warning($"Device {deviceId} reconnect policy does not allow reconnection.");
            return;
        }

        if (!stateMachine.CanRetry(policy.MaxAttempts ?? 100, TimeSpan.FromMinutes(10)))
        {
            _logger.Warning($"Device {deviceId} has exceeded maximum reconnect attempts.");
            return;
        }

        var cts = new CancellationTokenSource();
        _reconnectCts[deviceId] = cts;
        _ = ReconnectLoopAsync(deviceId, policy, cts.Token);
    }

    private async Task ReconnectLoopAsync(string deviceId, IReconnectPolicy policy, CancellationToken cancellationToken)
    {
        var attempt = 0;
        Exception? lastException = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;
            var delay = policy.GetNextDelay(attempt, lastException);
            if (delay == null)
            {
                _logger.Warning($"Device {deviceId} reconnect policy exhausted after {attempt} attempts.");
                break;
            }

            _logger.Info($"Reconnect scheduled. deviceId={deviceId} attempt={attempt} delayMs={delay.Value.TotalMilliseconds:F0}");

            try
            {
                await Task.Delay(delay.Value, cancellationToken).ConfigureAwait(false);
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

            if (!stateMachine.TransitionTo(DeviceStatus.Connecting, $"Reconnect attempt {attempt}"))
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
                    stateMachine.TransitionTo(DeviceStatus.Online, $"Reconnected after {attempt} attempts");
                    device.ReconnectCount = attempt;
                    _resourceMonitor?.RecordReconnect(deviceId, device.ChannelId, device.ProtocolId, device.ProtocolType, success: true);
                    _logger.Info($"Device reconnected. deviceId={deviceId} channelId={device.ChannelId} protocolId={device.ProtocolId} attempt={attempt}");
                    policy.Reset();
                    return;
                }

                lastException = new InvalidOperationException($"Reconnect attempt {attempt} failed");
                stateMachine.RecordError(lastException);
                stateMachine.TransitionTo(DeviceStatus.Error, $"Reconnect attempt {attempt} failed");
                _resourceMonitor?.RecordReconnect(deviceId, device.ChannelId, device.ProtocolId, device.ProtocolType, success: false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                stateMachine.RecordError(ex);
                stateMachine.TransitionTo(DeviceStatus.Error, $"Reconnect error: {ex.Message}");
                if (_devices.TryGetValue(deviceId, out var reconnectDevice))
                {
                    _resourceMonitor?.RecordReconnect(deviceId, reconnectDevice.ChannelId, reconnectDevice.ProtocolId, reconnectDevice.ProtocolType, success: false);
                }
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
}
