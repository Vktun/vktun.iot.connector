using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Api;

public interface IIoTDataCollector : IAsyncDisposable
{
    bool IsRunning { get; }
    int ConnectedDeviceCount { get; }
    
    Task InitializeAsync(SdkConfig? config = null);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    
    Task<bool> AddDeviceAsync(DeviceInfo device);
    Task<bool> RemoveDeviceAsync(string deviceId);
    Task<DeviceInfo?> GetDeviceAsync(string deviceId);
    Task<IEnumerable<DeviceInfo>> GetAllDevicesAsync();
    
    Task<bool> ConnectDeviceAsync(string deviceId);
    Task<bool> DisconnectDeviceAsync(string deviceId);
    
    Task<DeviceData?> CollectDataAsync(string deviceId, CancellationToken cancellationToken = default);
    Task<CommandResult> SendCommandAsync(DeviceCommand command, CancellationToken cancellationToken = default);
    
    Task<SdkConfig> GetConfigAsync();
    Task UpdateConfigAsync(SdkConfig config);
    
    event EventHandler<DeviceStatusChangedEventArgs>? DeviceStatusChanged;
    event EventHandler<DataReceivedEventArgs>? DataReceived;
    event EventHandler<DeviceErrorEventArgs>? DeviceError;
    event EventHandler<ResourceThresholdExceededEventArgs>? ResourceThresholdExceeded;
}

public class DeviceErrorEventArgs : EventArgs
{
    public string DeviceId { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public class CommandResult
{
    public string CommandId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public byte[]? ResponseData { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan ElapsedTime { get; set; }
}
