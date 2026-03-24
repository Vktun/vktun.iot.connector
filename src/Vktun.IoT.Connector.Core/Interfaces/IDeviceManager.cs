using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Core.Interfaces;

public interface IDeviceManager
{
    Task<bool> AddDeviceAsync(DeviceInfo device);
    Task<bool> RemoveDeviceAsync(string deviceId);
    Task<DeviceInfo?> GetDeviceAsync(string deviceId);
    Task<IEnumerable<DeviceInfo>> GetAllDevicesAsync();
    Task<IEnumerable<DeviceInfo>> GetDevicesByStatusAsync(DeviceStatus status);
    Task<bool> UpdateDeviceStatusAsync(string deviceId, DeviceStatus status);
    Task<bool> ConnectDeviceAsync(string deviceId);
    Task<bool> DisconnectDeviceAsync(string deviceId);
    Task<int> ConnectAllAsync();
    Task<int> DisconnectAllAsync();
    event EventHandler<DeviceStatusChangedEventArgs>? DeviceStatusChanged;
}

public class DeviceStatusChangedEventArgs : EventArgs
{
    public string DeviceId { get; set; } = string.Empty;
    public DeviceStatus OldStatus { get; set; }
    public DeviceStatus NewStatus { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string? Message { get; set; }
}
