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

    public override async Task<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (_isConnected)
        {
            return true;
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
            _logger.Info($"TCP服务器启动成功，端口: {_port}");
            
            _ = AcceptLoopAsync(_cancellationTokenSource.Token);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"TCP服务器启动失败: {ex.Message}", ex);
            return false;
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
        
        foreach (var connection in _connections.Values)
        {
            await DisconnectDeviceAsync(connection.DeviceId);
        }
        
        _listener?.Close();
        _listener?.Dispose();
        _listener = null;
        
        _logger.Info("TCP服务器已关闭");
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
            var bytesSent = await connection.Socket.SendAsync(data, SocketFlags.None, cancellationToken);
            connection.BytesSent += bytesSent;
            connection.LastActiveTime = DateTime.Now;
            return bytesSent;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(deviceId, "发送数据失败", ex);
            return 0;
        }
    }

    public override async IAsyncEnumerable<ReceivedData> ReceiveAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested && _isConnected)
        {
            await Task.Delay(100, cancellationToken);
            yield break;
        }
    }

    public override Task<bool> ConnectDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public override async Task DisconnectDeviceAsync(string deviceId)
    {
        if (_connections.TryRemove(deviceId, out var connection))
        {
            connection.Socket?.Shutdown(SocketShutdown.Both);
            connection.Socket?.Close();
            connection.Socket?.Dispose();
            connection.CancellationTokenSource?.Cancel();
            connection.CancellationTokenSource?.Dispose();
            
            OnDeviceDisconnected(deviceId, "主动断开");
            _logger.Info($"设备断开连接: {deviceId}");
        }
        
        await Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            try
            {
                var socket = await _listener.AcceptAsync(cancellationToken);
                var remoteEndPoint = socket.RemoteEndPoint as IPEndPoint;
                
                var connection = new DeviceConnection
                {
                    DeviceId = remoteEndPoint?.ToString() ?? Guid.NewGuid().ToString(),
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
                    IpAddress = remoteEndPoint?.Address.ToString() ?? "",
                    Port = remoteEndPoint?.Port ?? 0,
                    CommunicationType = CommunicationType.Tcp,
                    ConnectionMode = ConnectionMode.Server
                });
                
                _ = ReceiveLoopAsync(connection, connection.CancellationTokenSource.Token);
                
                _logger.Info($"新设备连接: {connection.DeviceId}");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error($"接受连接异常: {ex.Message}", ex);
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
                var bytesRead = await connection.Socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
                
                if (bytesRead == 0)
                {
                    await DisconnectDeviceAsync(connection.DeviceId);
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
                OnErrorOccurred(connection.DeviceId, "接收数据异常", ex);
                await DisconnectDeviceAsync(connection.DeviceId);
                break;
            }
        }
    }
}
