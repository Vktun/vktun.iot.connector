using System.Collections.Concurrent;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Business.Managers;

public class DeviceManager : IDeviceManager
{
    private readonly ConcurrentDictionary<string, DeviceInfo> _devices = new();
    private readonly ConcurrentDictionary<string, DeviceStatus> _deviceStatuses = new();
    private readonly ISessionManager _sessionManager;
    private readonly IDeviceCommandExecutor _commandExecutor;
    private readonly ILogger _logger;

    public event EventHandler<DeviceStatusChangedEventArgs>? DeviceStatusChanged;

    public DeviceManager(
        ISessionManager sessionManager,
        IDeviceCommandExecutor commandExecutor,
        ILogger logger)
    {
        _sessionManager = sessionManager;
        _commandExecutor = commandExecutor;
        _logger = logger;
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
            _logger.Info($"Device added: {device.DeviceId}");
        }

        return Task.FromResult(added);
    }

    public async Task<bool> RemoveDeviceAsync(string deviceId)
    {
        await DisconnectDeviceAsync(deviceId).ConfigureAwait(false);
        var removed = _devices.TryRemove(deviceId, out _);
        if (removed)
        {
            _deviceStatuses.TryRemove(deviceId, out _);
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

        await UpdateDeviceStatusAsync(deviceId, DeviceStatus.Connecting).ConfigureAwait(false);
        var connected = await _commandExecutor.ConnectAsync(device).ConfigureAwait(false);
        if (!connected)
        {
            await UpdateDeviceStatusAsync(deviceId, DeviceStatus.Error).ConfigureAwait(false);
            return false;
        }

        await _sessionManager.CreateSessionAsync(device).ConfigureAwait(false);
        await UpdateDeviceStatusAsync(deviceId, DeviceStatus.Online).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> DisconnectDeviceAsync(string deviceId)
    {
        var device = await GetDeviceAsync(deviceId).ConfigureAwait(false);
        if (device == null)
        {
            return false;
        }

        await UpdateDeviceStatusAsync(deviceId, DeviceStatus.Disconnecting).ConfigureAwait(false);
        await _commandExecutor.DisconnectAsync(deviceId).ConfigureAwait(false);
        await _sessionManager.RemoveSessionAsync(deviceId).ConfigureAwait(false);
        await UpdateDeviceStatusAsync(deviceId, DeviceStatus.Offline).ConfigureAwait(false);
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
}
