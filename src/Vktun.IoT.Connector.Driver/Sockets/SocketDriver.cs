using System.Net;
using System.Net.Sockets;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Driver.Sockets;

public interface ISocketDriver : IAsyncDisposable
{
    bool IsConnected { get; }
    IPEndPoint? LocalEndPoint { get; }
    IPEndPoint? RemoteEndPoint { get; }
    
    Task<bool> ConnectAsync(IPEndPoint remoteEndPoint, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task<int> SendAsync(byte[] data, CancellationToken cancellationToken = default);
    Task<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
    Task<int> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken = default);
    Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    void SetSocketOption(SocketOptionLevel level, SocketOptionName name, object value);
}

public class TcpSocketDriver : ISocketDriver
{
    private Socket? _socket;
    private readonly ILogger _logger;
    private readonly int _receiveBufferSize;
    private readonly int _sendBufferSize;
    private readonly bool _noDelay;
    private bool _isDisposed;

    public bool IsConnected => _socket?.Connected ?? false;
    public IPEndPoint? LocalEndPoint => _socket?.LocalEndPoint as IPEndPoint;
    public IPEndPoint? RemoteEndPoint => _socket?.RemoteEndPoint as IPEndPoint;

    public TcpSocketDriver(IConfigurationProvider configProvider, ILogger logger)
    {
        _logger = logger;
        var config = configProvider.GetConfig();
        _receiveBufferSize = config.Tcp.ReceiveBufferSize;
        _sendBufferSize = config.Tcp.SendBufferSize;
        _noDelay = config.Tcp.NoDelay;
    }

    public async Task<bool> ConnectAsync(IPEndPoint remoteEndPoint, CancellationToken cancellationToken = default)
    {
        try
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.ReceiveBufferSize = _receiveBufferSize;
            _socket.SendBufferSize = _sendBufferSize;
            _socket.NoDelay = _noDelay;
            
            await _socket.ConnectAsync(remoteEndPoint, cancellationToken);
            _logger.Info($"TCP连接成功: {remoteEndPoint}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"TCP连接失败: {ex.Message}", ex);
            return false;
        }
    }

    public Task DisconnectAsync()
    {
        if (_socket != null && _socket.Connected)
        {
            _socket.Shutdown(SocketShutdown.Both);
            _socket.Close();
            _socket.Dispose();
            _socket = null;
            _logger.Info("TCP连接已断开");
        }
        return Task.CompletedTask;
    }

    public async Task<int> SendAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        return await SendAsync(new ReadOnlyMemory<byte>(data), cancellationToken);
    }

    public async Task<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_socket == null || !_socket.Connected)
        {
            return 0;
        }

        try
        {
            return await _socket.SendAsync(data, SocketFlags.None, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error($"TCP发送数据失败: {ex.Message}", ex);
            return 0;
        }
    }

    public async Task<int> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken = default)
    {
        return await ReceiveAsync(new Memory<byte>(buffer), cancellationToken);
    }

    public async Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_socket == null || !_socket.Connected)
        {
            return 0;
        }

        try
        {
            return await _socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error($"TCP接收数据失败: {ex.Message}", ex);
            return 0;
        }
    }

    public void SetSocketOption(SocketOptionLevel level, SocketOptionName name, object value)
    {
        _socket?.SetSocketOption(level, name, value);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await DisconnectAsync();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}

public class UdpSocketDriver : ISocketDriver
{
    private Socket? _socket;
    private readonly ILogger _logger;
    private readonly int _receiveBufferSize;
    private bool _isDisposed;

    public bool IsConnected => _socket != null;
    public IPEndPoint? LocalEndPoint => _socket?.LocalEndPoint as IPEndPoint;
    public IPEndPoint? RemoteEndPoint => _socket?.RemoteEndPoint as IPEndPoint;

    public UdpSocketDriver(IConfigurationProvider configProvider, ILogger logger)
    {
        _logger = logger;
        var config = configProvider.GetConfig();
        _receiveBufferSize = config.Udp.ReceiveBufferSize;
    }

    public Task<bool> ConnectAsync(IPEndPoint remoteEndPoint, CancellationToken cancellationToken = default)
    {
        try
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.ReceiveBufferSize = _receiveBufferSize;
            _socket.Connect(remoteEndPoint);
            
            _logger.Info($"UDP绑定成功: {remoteEndPoint}");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.Error($"UDP绑定失败: {ex.Message}", ex);
            return Task.FromResult(false);
        }
    }

    public Task DisconnectAsync()
    {
        if (_socket != null)
        {
            _socket.Close();
            _socket.Dispose();
            _socket = null;
            _logger.Info("UDP已关闭");
        }
        return Task.CompletedTask;
    }

    public async Task<int> SendAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        return await SendAsync(new ReadOnlyMemory<byte>(data), cancellationToken);
    }

    public async Task<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_socket == null)
        {
            return 0;
        }

        try
        {
            return await _socket.SendAsync(data, SocketFlags.None, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error($"UDP发送数据失败: {ex.Message}", ex);
            return 0;
        }
    }

    public async Task<int> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken = default)
    {
        return await ReceiveAsync(new Memory<byte>(buffer), cancellationToken);
    }

    public async Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_socket == null)
        {
            return 0;
        }

        try
        {
            return await _socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error($"UDP接收数据失败: {ex.Message}", ex);
            return 0;
        }
    }

    public void SetSocketOption(SocketOptionLevel level, SocketOptionName name, object value)
    {
        _socket?.SetSocketOption(level, name, value);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await DisconnectAsync();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
