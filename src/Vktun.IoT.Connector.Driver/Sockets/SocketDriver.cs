using System.Net;
using System.Net.Sockets;
using Vktun.IoT.Connector.Core.Interfaces;

namespace Vktun.IoT.Connector.Driver.Sockets;

public interface ISocketDriver : IAsyncDisposable
{
    bool IsConnected { get; }
    IPEndPoint? LocalEndPoint { get; }
    IPEndPoint? RemoteEndPoint { get; }

    Task<bool> ConnectAsync(IPEndPoint remoteEndPoint, IPEndPoint? localEndPoint = null, CancellationToken cancellationToken = default);
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
        ArgumentNullException.ThrowIfNull(configProvider);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var config = configProvider.GetConfig();
        _receiveBufferSize = config.Tcp.ReceiveBufferSize;
        _sendBufferSize = config.Tcp.SendBufferSize;
        _noDelay = config.Tcp.NoDelay;
    }

    public async Task<bool> ConnectAsync(IPEndPoint remoteEndPoint, IPEndPoint? localEndPoint = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteEndPoint);

        try
        {
            await DisconnectAsync().ConfigureAwait(false);

            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                ReceiveBufferSize = _receiveBufferSize,
                SendBufferSize = _sendBufferSize,
                NoDelay = _noDelay
            };

            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            if (localEndPoint != null)
            {
                _socket.Bind(localEndPoint);
            }

            await _socket.ConnectAsync(remoteEndPoint, cancellationToken).ConfigureAwait(false);
            _logger.Info($"TCP connected: {remoteEndPoint}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"TCP connect failed: {ex.Message}", ex);
            await DisconnectAsync().ConfigureAwait(false);
            return false;
        }
    }

    public Task DisconnectAsync()
    {
        if (_socket == null)
        {
            return Task.CompletedTask;
        }

        try
        {
            if (_socket.Connected)
            {
                _socket.Shutdown(SocketShutdown.Both);
            }
        }
        catch (SocketException)
        {
        }
        finally
        {
            _socket.Close();
            _socket.Dispose();
            _socket = null;
        }

        _logger.Info("TCP disconnected.");
        return Task.CompletedTask;
    }

    public Task<int> SendAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        return SendAsync(new ReadOnlyMemory<byte>(data), cancellationToken);
    }

    public async Task<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_socket == null || !_socket.Connected)
        {
            return 0;
        }

        try
        {
            return await _socket.SendAsync(data, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"TCP send failed: {ex.Message}", ex);
            return 0;
        }
    }

    public Task<int> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken = default)
    {
        return ReceiveAsync(new Memory<byte>(buffer), cancellationToken);
    }

    public async Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_socket == null || !_socket.Connected)
        {
            return 0;
        }

        try
        {
            return await _socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"TCP receive failed: {ex.Message}", ex);
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

        await DisconnectAsync().ConfigureAwait(false);
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
        ArgumentNullException.ThrowIfNull(configProvider);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _receiveBufferSize = configProvider.GetConfig().Udp.ReceiveBufferSize;
    }

    public Task<bool> ConnectAsync(IPEndPoint remoteEndPoint, IPEndPoint? localEndPoint = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteEndPoint);

        try
        {
            _socket?.Dispose();
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                ReceiveBufferSize = _receiveBufferSize
            };

            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            if (localEndPoint != null)
            {
                _socket.Bind(localEndPoint);
            }

            _socket.Connect(remoteEndPoint);
            _logger.Info($"UDP connected: {remoteEndPoint}");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.Error($"UDP connect failed: {ex.Message}", ex);
            _socket?.Dispose();
            _socket = null;
            return Task.FromResult(false);
        }
    }

    public Task DisconnectAsync()
    {
        if (_socket == null)
        {
            return Task.CompletedTask;
        }

        _socket.Close();
        _socket.Dispose();
        _socket = null;
        _logger.Info("UDP disconnected.");
        return Task.CompletedTask;
    }

    public Task<int> SendAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        return SendAsync(new ReadOnlyMemory<byte>(data), cancellationToken);
    }

    public async Task<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_socket == null)
        {
            return 0;
        }

        try
        {
            return await _socket.SendAsync(data, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"UDP send failed: {ex.Message}", ex);
            return 0;
        }
    }

    public Task<int> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken = default)
    {
        return ReceiveAsync(new Memory<byte>(buffer), cancellationToken);
    }

    public async Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_socket == null)
        {
            return 0;
        }

        try
        {
            return await _socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"UDP receive failed: {ex.Message}", ex);
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

        await DisconnectAsync().ConfigureAwait(false);
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
