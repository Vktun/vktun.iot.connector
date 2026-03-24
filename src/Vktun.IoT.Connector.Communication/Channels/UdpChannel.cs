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

    public override async Task<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (_isConnected)
        {
            return true;
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
            _logger.Info($"UDP通道启动成功，端口: {_port}");
            
            _ = ReceiveLoopAsync(_cancellationTokenSource.Token);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"UDP通道启动失败: {ex.Message}", ex);
            return false;
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
        
        _logger.Info("UDP通道已关闭");
        return Task.CompletedTask;
    }

    public override Task<int> SendAsync(string deviceId, byte[] data, CancellationToken cancellationToken = default)
    {
        return SendAsync(deviceId, new ReadOnlyMemory<byte>(data), cancellationToken);
    }

    public override async Task<int> SendAsync(string deviceId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_socket == null || !_connections.TryGetValue(deviceId, out var connection))
        {
            return 0;
        }

        try
        {
            var bytesSent = await _socket.SendToAsync(data, SocketFlags.None, connection.RemoteEndPoint!, cancellationToken);
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
        
        OnDeviceDisconnected(deviceId, "主动断开");
        return Task.CompletedTask;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var config = _configProvider.GetConfig();
        var buffer = new byte[config.Udp.ReceiveBufferSize];
        var endPoint = new IPEndPoint(IPAddress.Any, 0) as EndPoint;
        
        while (!cancellationToken.IsCancellationRequested && _socket != null)
        {
            try
            {
                var bytesRead = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, endPoint, cancellationToken);
                
                if (bytesRead.ReceivedBytes > 0)
                {
                    var remoteEndPoint = bytesRead.RemoteEndPoint as IPEndPoint;
                    var deviceId = remoteEndPoint?.ToString() ?? Guid.NewGuid().ToString();
                    
                    var data = new byte[bytesRead.ReceivedBytes];
                    Buffer.BlockCopy(buffer, 0, data, 0, bytesRead.ReceivedBytes);
                    
                    if (!_connections.ContainsKey(deviceId))
                    {
                        var connection = new DeviceConnection
                        {
                            DeviceId = deviceId,
                            RemoteEndPoint = remoteEndPoint,
                            ConnectTime = DateTime.Now,
                            LastActiveTime = DateTime.Now
                        };
                        _connections[deviceId] = connection;
                    }
                    
                    _deviceLastActiveTimes[deviceId] = DateTime.Now;
                    OnDataReceived(deviceId, data);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error($"UDP接收数据异常: {ex.Message}", ex);
            }
        }
    }
}
