using System.Net;
using System.Net.Sockets;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;

namespace Vktun.IoT.Connector.Communication.Channels;

public class TcpServerChannel : CommunicationChannelBase
{
    private readonly IPAddress _localAddress;
    private readonly int _port;
    private readonly int _backlog;
    private readonly bool _allowAnonymousAcceptedClients;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    private Socket? _listener;
    private CancellationTokenSource? _lifetimeCts;
    private Task? _acceptLoopTask;
    private TaskCompletionSource<bool>? _pendingAcceptSource;
    private DeviceInfo? _expectedDevice;
    private IPAddress? _expectedRemoteAddress;

    public override CommunicationType CommunicationType => CommunicationType.Tcp;
    public override ConnectionMode ConnectionMode => ConnectionMode.Server;

    public TcpServerChannel(
        string localIpAddress,
        int port,
        IConfigurationProvider configProvider,
        ILogger logger,
        bool allowAnonymousAcceptedClients = false) : base(configProvider, logger)
    {
        _localAddress = string.IsNullOrWhiteSpace(localIpAddress)
            ? IPAddress.Any
            : IPAddress.Parse(localIpAddress);
        _port = port;
        ChannelId = $"TcpServer_{_localAddress}_{port}";
        _backlog = configProvider.GetConfig().Tcp.ListenBacklog;
        _allowAnonymousAcceptedClients = allowAnonymousAcceptedClients;
    }

    public override Task<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (_isConnected)
        {
            return Task.FromResult(true);
        }

        try
        {
            _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
            _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            var config = _configProvider.GetConfig();
            _listener.ReceiveBufferSize = config.Tcp.ReceiveBufferSize;
            _listener.SendBufferSize = config.Tcp.SendBufferSize;
            _listener.NoDelay = config.Tcp.NoDelay;

            _listener.Bind(new IPEndPoint(_localAddress, _port));
            _listener.Listen(_backlog);

            _isConnected = true;
            if (_allowAnonymousAcceptedClients)
            {
                EnsureAcceptLoopStarted();
            }
            _logger.Info($"TCP server is listening on {_localAddress}:{_port}.");
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
        _pendingAcceptSource?.TrySetCanceled();
        _pendingAcceptSource = null;
        _expectedDevice = null;
        _expectedRemoteAddress = null;

        _lifetimeCts?.Cancel();

        foreach (var connection in _connections.Values.ToArray())
        {
            await DisconnectDeviceAsync(connection.DeviceId).ConfigureAwait(false);
        }

        try
        {
            _listener?.Close();
            _listener?.Dispose();
        }
        finally
        {
            _listener = null;
        }

        if (_acceptLoopTask != null)
        {
            try
            {
                await _acceptLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _lifetimeCts?.Dispose();
        _lifetimeCts = null;
        _acceptLoopTask = null;

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
            OnDataSent(deviceId, data.ToArray(), bytesSent);
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
        await Task.CompletedTask.ConfigureAwait(false);
        throw new NotSupportedException("TcpServerChannel uses event-based data reception (DataReceived event). Use OnDataReceived instead of ReceiveAsync.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    public override async Task<bool> ConnectDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connections.ContainsKey(device.DeviceId))
            {
                return true;
            }

            var validation = ConnectionSettingsValidator.ValidateAndNormalize(device);
            if (!validation.IsValid || validation.Settings == null)
            {
                OnErrorOccurred(device.DeviceId, validation.ErrorMessage);
                return false;
            }

            _expectedDevice = CloneExpectedDevice(device);
            _expectedRemoteAddress = validation.Settings.RemoteAddress;
            _pendingAcceptSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            EnsureAcceptLoopStarted();

            var timeoutMs = _configProvider.GetConfig().Global.ConnectionTimeout;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts?.Token ?? CancellationToken.None);
            timeoutCts.CancelAfter(timeoutMs);

            try
            {
                return await _pendingAcceptSource.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                OnErrorOccurred(device.DeviceId, $"Timed out waiting for an incoming TCP client on {_localAddress}:{_port}.");
                return false;
            }
            finally
            {
                if (!_connections.ContainsKey(device.DeviceId))
                {
                    _expectedDevice = null;
                    _expectedRemoteAddress = null;
                }

                _pendingAcceptSource = null;
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public override Task DisconnectDeviceAsync(string deviceId)
    {
        if (_connections.TryRemove(deviceId, out var connection))
        {
            try
            {
                connection.Socket?.Shutdown(SocketShutdown.Both);
            }
            catch (SocketException)
            {
            }
            finally
            {
                connection.Socket?.Close();
                connection.Socket?.Dispose();
            }

            connection.CancellationTokenSource?.Cancel();
            connection.CancellationTokenSource?.Dispose();

            OnDeviceDisconnected(deviceId, "Disconnected.");
            _logger.Info($"TCP device disconnected: {deviceId}");
        }

        return Task.CompletedTask;
    }

    private void EnsureAcceptLoopStarted()
    {
        if (_acceptLoopTask != null || _listener == null || _lifetimeCts == null)
        {
            return;
        }

        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_lifetimeCts.Token), _lifetimeCts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            try
            {
                var socket = await _listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                var remoteEndPoint = socket.RemoteEndPoint as IPEndPoint;

                var expectedDevice = _expectedDevice;
                if (expectedDevice == null && !_allowAnonymousAcceptedClients)
                {
                    _logger.Warning($"Rejected unexpected TCP client {remoteEndPoint} because no device is waiting for a connection.");
                    socket.Dispose();
                    continue;
                }

                var deviceId = expectedDevice?.DeviceId ?? CreateAcceptedDeviceId(remoteEndPoint);
                if (_connections.ContainsKey(deviceId))
                {
                    _logger.Warning($"Rejected TCP client {remoteEndPoint} because device {deviceId} is already connected.");
                    socket.Dispose();
                    continue;
                }

                if (expectedDevice != null && !IsExpectedRemoteAddress(remoteEndPoint))
                {
                    _logger.Warning($"Rejected TCP client {remoteEndPoint} because it does not match the expected remote endpoint for {expectedDevice.DeviceId}.");
                    socket.Dispose();
                    continue;
                }

                var connection = new DeviceConnection
                {
                    DeviceId = deviceId,
                    Socket = socket,
                    RemoteEndPoint = remoteEndPoint,
                    ConnectTime = DateTime.Now,
                    LastActiveTime = DateTime.Now,
                    ReceiveBuffer = new byte[_configProvider.GetConfig().Tcp.ReceiveBufferSize],
                    CancellationTokenSource = new CancellationTokenSource()
                };

                _connections[connection.DeviceId] = connection;

                var connectedDevice = CreateConnectedDevice(expectedDevice, connection.DeviceId, remoteEndPoint);

                if (expectedDevice != null)
                {
                    _expectedDevice = null;
                    _expectedRemoteAddress = null;
                }

                OnDeviceConnected(connection.DeviceId, connectedDevice);
                _pendingAcceptSource?.TrySetResult(true);
                _ = ReceiveLoopAsync(connection, connection.CancellationTokenSource.Token);
                _logger.Info($"Accepted TCP client for device {connection.DeviceId}: {remoteEndPoint}");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to accept TCP connection: {ex.Message}", ex);
                _pendingAcceptSource?.TrySetException(ex);
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

    private bool IsExpectedRemoteAddress(IPEndPoint? remoteEndPoint)
    {
        if (remoteEndPoint == null || _expectedRemoteAddress == null)
        {
            return remoteEndPoint != null;
        }

        return Equals(remoteEndPoint.Address, _expectedRemoteAddress);
    }

    private string CreateAcceptedDeviceId(IPEndPoint? remoteEndPoint)
    {
        var endpointText = remoteEndPoint == null
            ? Guid.NewGuid().ToString("N")
            : $"{remoteEndPoint.Address}_{remoteEndPoint.Port}";
        var baseId = $"TCP_CLIENT_{endpointText}".Replace('.', '_').Replace(':', '_');

        return _connections.ContainsKey(baseId)
            ? $"{baseId}_{Guid.NewGuid():N}"
            : baseId;
    }

    private DeviceInfo CreateConnectedDevice(DeviceInfo? expectedDevice, string deviceId, IPEndPoint? remoteEndPoint)
    {
        var connectedDevice = expectedDevice == null
            ? new DeviceInfo
            {
                DeviceId = deviceId,
                DeviceName = $"TCP Client {remoteEndPoint}",
                CommunicationType = CommunicationType.Tcp,
                ConnectionMode = ConnectionMode.Server,
                LocalIpAddress = _localAddress.Equals(IPAddress.Any) ? string.Empty : _localAddress.ToString(),
                LocalPort = _port,
                ProtocolType = global::Vktun.IoT.Connector.Core.Enums.ProtocolType.Custom,
                ProtocolId = "TcpServerAcceptedClient"
            }
            : CloneExpectedDevice(expectedDevice);

        if (remoteEndPoint != null)
        {
            connectedDevice.IpAddress = remoteEndPoint.Address.ToString();
            connectedDevice.Port = remoteEndPoint.Port;
        }

        return connectedDevice;
    }

    private static DeviceInfo CloneExpectedDevice(DeviceInfo device)
    {
        return new DeviceInfo
        {
            DeviceId = device.DeviceId,
            DeviceName = device.DeviceName,
            ChannelId = device.ChannelId,
            CommunicationType = device.CommunicationType,
            ConnectionMode = device.ConnectionMode,
            IpAddress = device.IpAddress,
            Port = device.Port,
            LocalIpAddress = device.LocalIpAddress,
            LocalPort = device.LocalPort,
            SerialPort = device.SerialPort,
            BaudRate = device.BaudRate,
            SlaveId = device.SlaveId,
            ProtocolType = device.ProtocolType,
            ProtocolId = device.ProtocolId,
            ProtocolVersion = device.ProtocolVersion,
            ProtocolConfigPath = device.ProtocolConfigPath,
            Status = device.Status,
            LastConnectTime = device.LastConnectTime,
            LastDataTime = device.LastDataTime,
            ReconnectCount = device.ReconnectCount,
            ExtendedProperties = new Dictionary<string, object>(device.ExtendedProperties)
        };
    }
}
