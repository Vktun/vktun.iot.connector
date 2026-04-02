using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Communication.Channels;

public class UdpChannel : CommunicationChannelBase
{
    private Socket? _socket;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly int _port;
    private readonly ConcurrentDictionary<string, DateTime> _deviceLastActiveTimes;

    public override CommunicationType CommunicationType => CommunicationType.Udp;
    public override ConnectionMode ConnectionMode => ConnectionMode.Server;

    public UdpChannel(
        int port,
        IConfigurationProvider configProvider,
        ILogger logger) : base(configProvider, logger)
    {
        _port = port;
        ChannelId = $"Udp_{port}";
        _deviceLastActiveTimes = new ConcurrentDictionary<string, DateTime>();
    }

    public override Task<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (_isConnected)
        {
            return Task.FromResult(true);
        }

        try
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            var config = _configProvider.GetConfig();
            _socket.ReceiveBufferSize = config.Udp.ReceiveBufferSize;
            _socket.Bind(new IPEndPoint(IPAddress.Any, _port));

            _isConnected = true;
            _logger.Info($"UDP channel started on port {_port}.");

            _ = ReceiveLoopAsync(_cancellationTokenSource.Token);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to start UDP channel: {ex.Message}", ex);
            return Task.FromResult(false);
        }
    }

    public override Task CloseAsync()
    {
        if (!_isConnected)
        {
            return Task.CompletedTask;
        }

        _isConnected = false;
        _cancellationTokenSource?.Cancel();

        _socket?.Close();
        _socket?.Dispose();
        _socket = null;

        _connections.Clear();
        _deviceLastActiveTimes.Clear();

        _logger.Info("UDP channel stopped.");
        return Task.CompletedTask;
    }

    public override Task<int> SendAsync(string deviceId, byte[] data, CancellationToken cancellationToken = default)
    {
        return SendAsync(deviceId, new ReadOnlyMemory<byte>(data), cancellationToken);
    }

    public override async Task<int> SendAsync(string deviceId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_socket == null || !_connections.TryGetValue(deviceId, out var connection) || connection.RemoteEndPoint == null)
        {
            return 0;
        }

        try
        {
            var bytesSent = await _socket.SendToAsync(data, SocketFlags.None, connection.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
            connection.BytesSent += bytesSent;
            connection.LastActiveTime = DateTime.Now;
            return bytesSent;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(deviceId, "Failed to send UDP data.", ex);
            return 0;
        }
    }

    public override async IAsyncEnumerable<ReceivedData> ReceiveAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested && _isConnected)
        {
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            yield break;
        }
    }

    public override Task<bool> ConnectDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        var endPoint = new IPEndPoint(IPAddress.Parse(device.IpAddress), device.Port);
        var connection = new DeviceConnection
        {
            DeviceId = device.DeviceId,
            RemoteEndPoint = endPoint,
            ConnectTime = DateTime.Now,
            LastActiveTime = DateTime.Now
        };

        _connections[device.DeviceId] = connection;
        _deviceLastActiveTimes[device.DeviceId] = DateTime.Now;

        OnDeviceConnected(device.DeviceId, device);
        return Task.FromResult(true);
    }

    public override Task DisconnectDeviceAsync(string deviceId)
    {
        _connections.TryRemove(deviceId, out _);
        _deviceLastActiveTimes.TryRemove(deviceId, out _);

        OnDeviceDisconnected(deviceId, "Disconnected.");
        return Task.CompletedTask;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var config = _configProvider.GetConfig();
        var buffer = new byte[config.Udp.ReceiveBufferSize];
        EndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);

        while (!cancellationToken.IsCancellationRequested && _socket != null)
        {
            try
            {
                var receiveResult = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, endPoint, cancellationToken).ConfigureAwait(false);

                if (receiveResult.ReceivedBytes <= 0)
                {
                    continue;
                }

                var remoteEndPoint = receiveResult.RemoteEndPoint as IPEndPoint;
                var deviceId = ResolveDeviceId(remoteEndPoint);

                var data = new byte[receiveResult.ReceivedBytes];
                Buffer.BlockCopy(buffer, 0, data, 0, receiveResult.ReceivedBytes);

                if (!_connections.TryGetValue(deviceId, out var connection))
                {
                    connection = new DeviceConnection
                    {
                        DeviceId = deviceId,
                        RemoteEndPoint = remoteEndPoint,
                        ConnectTime = DateTime.Now,
                        LastActiveTime = DateTime.Now
                    };
                    _connections[deviceId] = connection;
                }

                connection.BytesReceived += receiveResult.ReceivedBytes;
                connection.LastActiveTime = DateTime.Now;
                _deviceLastActiveTimes[deviceId] = DateTime.Now;

                OnDataReceived(deviceId, data);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to receive UDP data: {ex.Message}", ex);
            }
        }
    }

    private string ResolveDeviceId(IPEndPoint? remoteEndPoint)
    {
        if (remoteEndPoint == null)
        {
            return Guid.NewGuid().ToString("N");
        }

        foreach (var pair in _connections)
        {
            if (pair.Value.RemoteEndPoint is IPEndPoint knownEndPoint &&
                Equals(knownEndPoint.Address, remoteEndPoint.Address) &&
                knownEndPoint.Port == remoteEndPoint.Port)
            {
                return pair.Key;
            }
        }

        return remoteEndPoint.ToString();
    }
}
