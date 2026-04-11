using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Communication.Channels;

public abstract class CommunicationChannelBase : ICommunicationChannel
{
    protected readonly ILogger _logger;
    protected readonly IConfigurationProvider _configProvider;
    protected readonly ConcurrentDictionary<string, DeviceConnection> _connections;
    protected bool _isConnected;
    protected bool _isDisposed;
    private readonly ChannelStatistics _statistics = new();

    public string ChannelId { get; protected set; } = string.Empty;
    public abstract CommunicationType CommunicationType { get; }
    public abstract ConnectionMode ConnectionMode { get; }
    public bool IsConnected => _isConnected;
    public int ActiveConnections => _connections.Count;
    public ChannelStatistics Statistics => _statistics.Snapshot();

    public event EventHandler<ChannelErrorEventArgs>? ErrorOccurred;
    public event EventHandler<DeviceConnectedEventArgs>? DeviceConnected;
    public event EventHandler<DeviceDisconnectedEventArgs>? DeviceDisconnected;
    public event EventHandler<DataReceivedEventArgs>? DataReceived;
    public event EventHandler<DataSentEventArgs>? DataSent;

    protected CommunicationChannelBase(IConfigurationProvider configProvider, ILogger logger)
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

    public ChannelStatistics ResetStatistics()
    {
        var snapshot = _statistics.Snapshot();
        _statistics.TotalBytesSent = 0;
        _statistics.TotalBytesReceived = 0;
        _statistics.TotalPacketsSent = 0;
        _statistics.TotalPacketsReceived = 0;
        _statistics.TotalErrors = 0;
        _statistics.TotalConnections = 0;
        _statistics.TotalDisconnections = 0;
        _statistics.LastSendTime = null;
        _statistics.LastReceiveTime = null;
        _statistics.LastErrorTime = null;
        _statistics.StartTime = DateTime.Now;
        return snapshot;
    }

    protected virtual void OnErrorOccurred(string deviceId, string message, Exception? exception = null)
    {
        _statistics.TotalErrors++;
        _statistics.LastErrorTime = DateTime.Now;
        ErrorOccurred?.Invoke(this, new ChannelErrorEventArgs
        {
            DeviceId = deviceId,
            Message = message,
            Exception = exception
        });
    }

    protected virtual void OnDeviceConnected(string deviceId, DeviceInfo device)
    {
        _statistics.TotalConnections++;
        DeviceConnected?.Invoke(this, new DeviceConnectedEventArgs
        {
            DeviceId = deviceId,
            Device = device,
            Timestamp = DateTime.Now
        });
    }

    protected virtual void OnDeviceDisconnected(string deviceId, string reason)
    {
        _statistics.TotalDisconnections++;
        DeviceDisconnected?.Invoke(this, new DeviceDisconnectedEventArgs
        {
            DeviceId = deviceId,
            Reason = reason,
            Timestamp = DateTime.Now
        });
    }

    protected virtual void OnDataReceived(string deviceId, byte[] data)
    {
        _statistics.TotalBytesReceived += data.Length;
        _statistics.TotalPacketsReceived++;
        _statistics.LastReceiveTime = DateTime.Now;
        DataReceived?.Invoke(this, new DataReceivedEventArgs
        {
            DeviceId = deviceId,
            Data = data,
            Timestamp = DateTime.Now
        });
    }

    protected virtual void OnDataSent(string deviceId, byte[] data, int bytesSent)
    {
        _statistics.TotalBytesSent += bytesSent;
        _statistics.TotalPacketsSent++;
        _statistics.LastSendTime = DateTime.Now;
        DataSent?.Invoke(this, new DataSentEventArgs
        {
            DeviceId = deviceId,
            Data = data,
            BytesSent = bytesSent,
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
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

public class DeviceConnection
{
    public string DeviceId { get; set; } = string.Empty;
    public Socket? Socket { get; set; }
    public IPEndPoint? RemoteEndPoint { get; set; }
    public DateTime ConnectTime { get; set; }
    public DateTime LastActiveTime { get; set; }
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }
    public byte[] ReceiveBuffer { get; set; } = Array.Empty<byte>();
    public CancellationTokenSource? CancellationTokenSource { get; set; }
}
