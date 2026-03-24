using System.Collections.Concurrent;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Business.Managers;

public class DeviceManager : IDeviceManager
{
    private readonly ConcurrentDictionary<string, DeviceInfo> _devices;
    private readonly ConcurrentDictionary<string, DeviceStatus> _deviceStatuses;
    private readonly ISessionManager _sessionManager;
    private readonly IConfigurationProvider _configProvider;
    private readonly ILogger _logger;

    public event EventHandler<DeviceStatusChangedEventArgs>? DeviceStatusChanged;

    public DeviceManager(
        ISessionManager sessionManager,
        IConfigurationProvider configProvider,
        ILogger logger)
    {
        _devices = new ConcurrentDictionary<string, DeviceInfo>();
        _deviceStatuses = new ConcurrentDictionary<string, DeviceStatus>();
        _sessionManager = sessionManager;
        _configProvider = configProvider;
        _logger = logger;
    }

    public Task<bool> AddDeviceAsync(DeviceInfo device)
    {
        if (string.IsNullOrEmpty(device.DeviceId))
        {
            return Task.FromResult(false);
        }

        var result = _devices.TryAdd(device.DeviceId, device);
        if (result)
        {
            _deviceStatuses[device.DeviceId] = DeviceStatus.Offline;
            _logger.Info($"设备添加成功: {device.DeviceId}");
        }
        return Task.FromResult(result);
    }

    public Task<bool> RemoveDeviceAsync(string deviceId)
    {
        var result = _devices.TryRemove(deviceId, out _);
        if (result)
        {
            _deviceStatuses.TryRemove(deviceId, out _);
            _sessionManager.RemoveSessionAsync(deviceId);
            _logger.Info($"设备移除成功: {deviceId}");
        }
        return Task.FromResult(result);
    }

    public Task<DeviceInfo?> GetDeviceAsync(string deviceId)
    {
        _devices.TryGetValue(deviceId, out var device);
        return Task.FromResult(device);
    }

    public Task<IEnumerable<DeviceInfo>> GetAllDevicesAsync()
    {
        return Task.FromResult(_devices.Values.AsEnumerable());
    }

    public Task<IEnumerable<DeviceInfo>> GetDevicesByStatusAsync(DeviceStatus status)
    {
        var devices = _devices.Values
            .Where(d => _deviceStatuses.TryGetValue(d.DeviceId, out var s) && s == status);
        return Task.FromResult(devices);
    }

    public Task<bool> UpdateDeviceStatusAsync(string deviceId, DeviceStatus status)
    {
        if (!_deviceStatuses.TryGetValue(deviceId, out var oldStatus))
        {
            return Task.FromResult(false);
        }

        _deviceStatuses[deviceId] = status;
        
        if (_devices.TryGetValue(deviceId, out var device))
        {
            device.Status = status;
            device.LastConnectTime = status == DeviceStatus.Online ? DateTime.Now : device.LastConnectTime;
        }

        if (oldStatus != status)
        {
            DeviceStatusChanged?.Invoke(this, new DeviceStatusChangedEventArgs
            {
                DeviceId = deviceId,
                OldStatus = oldStatus,
                NewStatus = status,
                Timestamp = DateTime.Now
            });
        }

        return Task.FromResult(true);
    }

    public async Task<bool> ConnectDeviceAsync(string deviceId)
    {
        var device = await GetDeviceAsync(deviceId);
        if (device == null)
        {
            return false;
        }

        await UpdateDeviceStatusAsync(deviceId, DeviceStatus.Connecting);
        
        var session = await _sessionManager.CreateSessionAsync(device);
        if (session != null)
        {
            await UpdateDeviceStatusAsync(deviceId, DeviceStatus.Online);
            return true;
        }

        await UpdateDeviceStatusAsync(deviceId, DeviceStatus.Error);
        return false;
    }

    public async Task<bool> DisconnectDeviceAsync(string deviceId)
    {
        var device = await GetDeviceAsync(deviceId);
        if (device == null)
        {
            return false;
        }

        await UpdateDeviceStatusAsync(deviceId, DeviceStatus.Disconnecting);
        await _sessionManager.RemoveSessionAsync(deviceId);
        await UpdateDeviceStatusAsync(deviceId, DeviceStatus.Offline);
        
        return true;
    }

    public async Task<int> ConnectAllAsync()
    {
        var devices = await GetAllDevicesAsync();
        var tasks = devices.Select(d => ConnectDeviceAsync(d.DeviceId));
        var results = await Task.WhenAll(tasks);
        return results.Count(r => r);
    }

    public async Task<int> DisconnectAllAsync()
    {
        var devices = await GetAllDevicesAsync();
        var tasks = devices.Select(d => DisconnectDeviceAsync(d.DeviceId));
        var results = await Task.WhenAll(tasks);
        return results.Count(r => r);
    }
}
