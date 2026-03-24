using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Core.Interfaces;

public interface ICommunicationChannel : IAsyncDisposable, IDisposable
{
    string ChannelId { get; }
    CommunicationType CommunicationType { get; }
    ConnectionMode ConnectionMode { get; }
    bool IsConnected { get; }
    int ActiveConnections { get; }
    
    Task<bool> OpenAsync(CancellationToken cancellationToken = default);
    Task CloseAsync();
    Task<int> SendAsync(string deviceId, byte[] data, CancellationToken cancellationToken = default);
    Task<int> SendAsync(string deviceId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ReceivedData> ReceiveAsync(CancellationToken cancellationToken = default);
    Task<bool> ConnectDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default);
    Task DisconnectDeviceAsync(string deviceId);
    
    event EventHandler<ChannelErrorEventArgs>? ErrorOccurred;
    event EventHandler<DeviceConnectedEventArgs>? DeviceConnected;
    event EventHandler<DeviceDisconnectedEventArgs>? DeviceDisconnected;
    event EventHandler<DataReceivedEventArgs>? DataReceived;
}

public class ReceivedData
{
    public string DeviceId { get; set; } = string.Empty;
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public class ChannelErrorEventArgs : EventArgs
{
    public string DeviceId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}

public class DeviceConnectedEventArgs : EventArgs
{
    public string DeviceId { get; set; } = string.Empty;
    public DeviceInfo Device { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public class DeviceDisconnectedEventArgs : EventArgs
{
    public string DeviceId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public class DataReceivedEventArgs : EventArgs
{
    public string DeviceId { get; set; } = string.Empty;
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
