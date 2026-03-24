using System.Collections.Concurrent;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Serial.Channels;

public abstract class SerialChannelBase : ICommunicationChannel
{
    protected readonly ILogger _logger;
    protected readonly IConfigurationProvider _configProvider;
    protected readonly ConcurrentDictionary<string, DeviceConnection> _connections;
    protected bool _isConnected;
    protected bool _isDisposed;

    public string ChannelId { get; protected set; } = string.Empty;
    public abstract CommunicationType CommunicationType { get; }
    public abstract ConnectionMode ConnectionMode { get; }
    public bool IsConnected => _isConnected;
    public int ActiveConnections => _connections.Count;

    public event EventHandler<ChannelErrorEventArgs>? ErrorOccurred;
    public event EventHandler<DeviceConnectedEventArgs>? DeviceConnected;
    public event EventHandler<DeviceDisconnectedEventArgs>? DeviceDisconnected;
    public event EventHandler<DataReceivedEventArgs>? DataReceived;

    protected SerialChannelBase(IConfigurationProvider configProvider, ILogger logger)
    {
        _configProvider = configProvider;
        _logger = logger;
        _connections = new ConcurrentDictionary<string, DeviceConnection>();
    }

    public abstract Task<bool> OpenAsync(CancellationToken cancellationToken = default);
    public abstract Task CloseAsync();
    public abstract Task<int> SendAsync(string deviceId, byte[] data, CancellationToken cancellationToken = default);
    public abstract Task<int> SendAsync(string deviceId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
    public abstract IAsyncEnumerable<ReceivedData> ReceiveAsync(CancellationToken cancellationToken = default);
    public abstract Task<bool> ConnectDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default);
    public abstract Task DisconnectDeviceAsync(string deviceId);

    protected virtual void OnErrorOccurred(string deviceId, string message, Exception? exception = null)
    {
        ErrorOccurred?.Invoke(this, new ChannelErrorEventArgs
        {
            DeviceId = deviceId,
            Message = message,
            Exception = exception
        });
    }

    protected virtual void OnDeviceConnected(string deviceId, DeviceInfo device)
    {
        DeviceConnected?.Invoke(this, new DeviceConnectedEventArgs
        {
            DeviceId = deviceId,
            Device = device,
            Timestamp = DateTime.Now
        });
    }

    protected virtual void OnDeviceDisconnected(string deviceId, string reason)
    {
        DeviceDisconnected?.Invoke(this, new DeviceDisconnectedEventArgs
        {
            DeviceId = deviceId,
            Reason = reason,
            Timestamp = DateTime.Now
        });
    }

    protected virtual void OnDataReceived(string deviceId, byte[] data)
    {
        DataReceived?.Invoke(this, new DataReceivedEventArgs
        {
            DeviceId = deviceId,
            Data = data,
            Timestamp = DateTime.Now
        });
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await CloseAsync();
        _isDisposed = true;
        
        GC.SuppressFinalize(this);
    }

    public virtual void Dispose()
    {
        DisposeAsync().AsTask().Wait();
    }
}

public class DeviceConnection
{
    public string DeviceId { get; set; } = string.Empty;
    public DateTime ConnectTime { get; set; }
    public DateTime LastActiveTime { get; set; }
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }
    public byte[] ReceiveBuffer { get; set; } = Array.Empty<byte>();
    public CancellationTokenSource? CancellationTokenSource { get; set; }
}
