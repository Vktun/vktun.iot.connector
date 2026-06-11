using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Communication.Channels;

public sealed class WirelessIpChannel : CommunicationChannelBase
{
    private readonly ICommunicationChannel _innerChannel;
    private readonly CommunicationType _communicationType;
    private readonly ConnectionMode _mode;

    public override CommunicationType CommunicationType => _communicationType;
    public override ConnectionMode ConnectionMode => _mode;

    public WirelessIpChannel(
        CommunicationType communicationType,
        ConnectionMode mode,
        string localIpAddress,
        int localPort,
        IConfigurationProvider configProvider,
        ILogger logger)
        : base(configProvider, logger)
    {
        if (communicationType is not (CommunicationType.FourG or CommunicationType.NbIoT))
        {
            throw new ArgumentOutOfRangeException(nameof(communicationType), communicationType, "Wireless IP channel supports FourG and NbIoT only.");
        }

        _communicationType = communicationType;
        _mode = mode;
        _innerChannel = mode == ConnectionMode.Server
            ? new TcpServerChannel(localIpAddress, localPort, configProvider, logger)
            : new TcpClientChannel(configProvider, logger);

        _innerChannel.ErrorOccurred += OnInnerErrorOccurred;
        _innerChannel.DeviceConnected += OnInnerDeviceConnected;
        _innerChannel.DeviceDisconnected += OnInnerDeviceDisconnected;
        _innerChannel.DataReceived += OnInnerDataReceived;
        _innerChannel.DataSent += OnInnerDataSent;
        ChannelId = $"{communicationType}_{mode}_{localIpAddress}_{localPort}";
    }

    public override async Task<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        var opened = await _innerChannel.OpenAsync(cancellationToken).ConfigureAwait(false);
        _isConnected = opened;
        return opened;
    }

    public override async Task CloseAsync()
    {
        await _innerChannel.CloseAsync().ConfigureAwait(false);
        _connections.Clear();
        _isConnected = false;
    }

    public override Task<int> SendAsync(string deviceId, byte[] data, CancellationToken cancellationToken = default)
    {
        return _innerChannel.SendAsync(deviceId, data, cancellationToken);
    }

    public override Task<int> SendAsync(string deviceId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        return _innerChannel.SendAsync(deviceId, data, cancellationToken);
    }

    public override async IAsyncEnumerable<ReceivedData> ReceiveAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        throw new NotSupportedException("WirelessIpChannel uses event-based data reception (DataReceived event).");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    public override async Task<bool> ConnectDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        var connected = await _innerChannel.ConnectDeviceAsync(device, cancellationToken).ConfigureAwait(false);
        _isConnected = _innerChannel.IsConnected;
        if (connected)
        {
            TrackConnection(device.DeviceId, device);
        }

        return connected;
    }

    public override async Task DisconnectDeviceAsync(string deviceId)
    {
        await _innerChannel.DisconnectDeviceAsync(deviceId).ConfigureAwait(false);
        _connections.TryRemove(deviceId, out _);
    }

    private void OnInnerErrorOccurred(object? sender, ChannelErrorEventArgs args)
    {
        OnErrorOccurred(args.DeviceId, args.Message, args.Exception);
    }

    private void OnInnerDeviceConnected(object? sender, DeviceConnectedEventArgs args)
    {
        TrackConnection(args.DeviceId, args.Device);
        OnDeviceConnected(args.DeviceId, args.Device);
    }

    private void OnInnerDeviceDisconnected(object? sender, DeviceDisconnectedEventArgs args)
    {
        _connections.TryRemove(args.DeviceId, out _);
        OnDeviceDisconnected(args.DeviceId, args.Reason);
    }

    private void OnInnerDataReceived(object? sender, DataReceivedEventArgs args)
    {
        if (_connections.TryGetValue(args.DeviceId, out var connection))
        {
            connection.AddBytesReceived(args.Data.Length);
            connection.LastActiveTime = DateTime.Now;
        }

        OnDataReceived(args.DeviceId, args.Data);
    }

    private void OnInnerDataSent(object? sender, DataSentEventArgs args)
    {
        if (_connections.TryGetValue(args.DeviceId, out var connection))
        {
            connection.AddBytesSent(args.BytesSent);
            connection.LastActiveTime = DateTime.Now;
        }

        OnDataSent(args.DeviceId, args.Data, args.BytesSent);
    }

    private void TrackConnection(string deviceId, DeviceInfo device)
    {
        _connections[deviceId] = new DeviceConnection
        {
            DeviceId = deviceId,
            ConnectTime = DateTime.Now,
            LastActiveTime = DateTime.Now
        };

        ChannelId = $"{_communicationType}_{_mode}_{_innerChannel.ChannelId}";
    }

    public override async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _innerChannel.ErrorOccurred -= OnInnerErrorOccurred;
        _innerChannel.DeviceConnected -= OnInnerDeviceConnected;
        _innerChannel.DeviceDisconnected -= OnInnerDeviceDisconnected;
        _innerChannel.DataReceived -= OnInnerDataReceived;
        _innerChannel.DataSent -= OnInnerDataSent;

        await _innerChannel.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
