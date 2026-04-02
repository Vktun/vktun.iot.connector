using System.Net;
using System.Net.Sockets;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Communication.Channels;

public class TcpServerChannel : CommunicationChannelBase
{
    private Socket? _listener;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly int _port;
    private readonly int _backlog;

    public override CommunicationType CommunicationType => CommunicationType.Tcp;
    public override ConnectionMode ConnectionMode => ConnectionMode.Server;

    public TcpServerChannel(
        int port,
        IConfigurationProvider configProvider,
        ILogger logger) : base(configProvider, logger)
    {
        _port = port;
        ChannelId = $"TcpServer_{port}";

        var config = configProvider.GetConfig();
        _backlog = config.Tcp.ListenBacklog;
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

            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
            _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            var config = _configProvider.GetConfig();
            _listener.ReceiveBufferSize = config.Tcp.ReceiveBufferSize;
            _listener.SendBufferSize = config.Tcp.SendBufferSize;
            _listener.NoDelay = config.Tcp.NoDelay;

            _listener.Bind(new IPEndPoint(IPAddress.Any, _port));
            _listener.Listen(_backlog);

            _isConnected = true;
            _logger.Info($"TCP server started on port {_port}.");

            _ = AcceptLoopAsync(_cancellationTokenSource.Token);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to start TCP server: {ex.Message}", ex);
            return Task.FromResult(false);
        }
    }

    public override async Task CloseAsync()
    {
        if (!_isConnected)
        {
            return;
        }

        _isConnected = false;
        _cancellationTokenSource?.Cancel();

        foreach (var connection in _connections.Values.ToArray())
        {
            await DisconnectDeviceAsync(connection.DeviceId).ConfigureAwait(false);
        }

        _listener?.Close();
        _listener?.Dispose();
        _listener = null;

        _logger.Info("TCP server stopped.");
    }

    public override Task<int> SendAsync(string deviceId, byte[] data, CancellationToken cancellationToken = default)
    {
        return SendAsync(deviceId, new ReadOnlyMemory<byte>(data), cancellationToken);
    }

    public override async Task<int> SendAsync(string deviceId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(deviceId, out var connection) || connection.Socket == null)
        {
            return 0;
        }

        try
        {
            var bytesSent = await connection.Socket.SendAsync(data, SocketFlags.None, cancellationToken).ConfigureAwait(false);
            connection.BytesSent += bytesSent;
            connection.LastActiveTime = DateTime.Now;
            return bytesSent;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(deviceId, "Failed to send TCP data.", ex);
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
        return Task.FromResult(true);
    }

    public override Task DisconnectDeviceAsync(string deviceId)
    {
        if (_connections.TryRemove(deviceId, out var connection))
        {
            try
            {
                connection.Socket?.Shutdown(SocketShutdown.Both);
            }
            catch
            {
            }

            connection.Socket?.Close();
            connection.Socket?.Dispose();
            connection.CancellationTokenSource?.Cancel();
            connection.CancellationTokenSource?.Dispose();

            OnDeviceDisconnected(deviceId, "Disconnected.");
            _logger.Info($"Device disconnected: {deviceId}");
        }

        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            try
            {
                var socket = await _listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                var remoteEndPoint = socket.RemoteEndPoint as IPEndPoint;

                var connection = new DeviceConnection
                {
                    DeviceId = remoteEndPoint?.ToString() ?? Guid.NewGuid().ToString("N"),
                    Socket = socket,
                    RemoteEndPoint = remoteEndPoint,
                    ConnectTime = DateTime.Now,
                    LastActiveTime = DateTime.Now,
                    ReceiveBuffer = new byte[_configProvider.GetConfig().Tcp.ReceiveBufferSize],
                    CancellationTokenSource = new CancellationTokenSource()
                };

                _connections[connection.DeviceId] = connection;

                OnDeviceConnected(connection.DeviceId, new DeviceInfo
                {
                    DeviceId = connection.DeviceId,
                    IpAddress = remoteEndPoint?.Address.ToString() ?? string.Empty,
                    Port = remoteEndPoint?.Port ?? 0,
                    CommunicationType = CommunicationType.Tcp,
                    ConnectionMode = ConnectionMode.Server
                });

                _ = ReceiveLoopAsync(connection, connection.CancellationTokenSource.Token);
                _logger.Info($"Accepted device connection: {connection.DeviceId}");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to accept TCP connection: {ex.Message}", ex);
            }
        }
    }

    private async Task ReceiveLoopAsync(DeviceConnection connection, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && connection.Socket != null)
        {
            try
            {
                var buffer = connection.ReceiveBuffer;
                var bytesRead = await connection.Socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    await DisconnectDeviceAsync(connection.DeviceId).ConfigureAwait(false);
                    break;
                }

                connection.BytesReceived += bytesRead;
                connection.LastActiveTime = DateTime.Now;

                var data = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, data, 0, bytesRead);

                OnDataReceived(connection.DeviceId, data);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnErrorOccurred(connection.DeviceId, "Failed to receive TCP data.", ex);
                await DisconnectDeviceAsync(connection.DeviceId).ConfigureAwait(false);
                break;
            }
        }
    }
}
