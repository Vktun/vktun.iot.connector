using System.Net;
using System.Net.Sockets;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;

namespace Vktun.IoT.Connector.Communication.Channels;

public class UdpChannel : CommunicationChannelBase
{
    private readonly ConnectionMode _mode;
    private readonly IPAddress _localAddress;
    private readonly int _localPort;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    private Socket? _socket;
    private CancellationTokenSource? _lifetimeCts;
    private Task? _receiveLoopTask;
    private DeviceInfo? _expectedDevice;
    private IPAddress? _expectedRemoteAddress;
    private TaskCompletionSource<bool>? _pendingFirstPacketSource;

    public override CommunicationType CommunicationType => CommunicationType.Udp;
    public override ConnectionMode ConnectionMode => _mode;

    public UdpChannel(
        ConnectionMode mode,
        string localIpAddress,
        int localPort,
        IConfigurationProvider configProvider,
        ILogger logger) : base(configProvider, logger)
    {
        _mode = mode;
        _localAddress = string.IsNullOrWhiteSpace(localIpAddress) ? IPAddress.Any : IPAddress.Parse(localIpAddress);
        _localPort = localPort;
        ChannelId = $"Udp_{mode}_{_localAddress}_{localPort}";
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
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.ReceiveBufferSize = _configProvider.GetConfig().Udp.ReceiveBufferSize;

            if (_mode == ConnectionMode.Server)
            {
                _socket.Bind(new IPEndPoint(_localAddress, _localPort));
                _logger.Info($"UDP server is listening on {_localAddress}:{_localPort}.");
            }
            else if (_localPort > 0 || !_localAddress.Equals(IPAddress.Any))
            {
                _socket.Bind(new IPEndPoint(_localAddress, _localPort));
                _logger.Info($"UDP client bound local endpoint {_localAddress}:{_localPort}.");
            }

            _isConnected = true;
            _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_lifetimeCts.Token), _lifetimeCts.Token);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to open UDP channel: {ex.Message}", ex);
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
        _pendingFirstPacketSource?.TrySetCanceled();
        _pendingFirstPacketSource = null;
        _expectedDevice = null;
        _expectedRemoteAddress = null;

        _lifetimeCts?.Cancel();

        foreach (var deviceId in _connections.Keys.ToArray())
        {
            await DisconnectDeviceAsync(deviceId).ConfigureAwait(false);
        }

        _socket?.Close();
        _socket?.Dispose();
        _socket = null;

        if (_receiveLoopTask != null)
        {
            try
            {
                await _receiveLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _lifetimeCts?.Dispose();
        _lifetimeCts = null;
        _receiveLoopTask = null;

        _logger.Info("UDP channel stopped.");
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
        await Task.CompletedTask.ConfigureAwait(false);
        throw new NotSupportedException("UdpChannel uses event-based data reception (DataReceived event). Use OnDataReceived instead of ReceiveAsync.");
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

            var settings = validation.Settings;
            if (_socket == null)
            {
                OnErrorOccurred(device.DeviceId, "UDP socket is not initialized.");
                return false;
            }

            if (_mode == ConnectionMode.Client)
            {
                if (settings.RemoteAddress == null || settings.RemotePort <= 0)
                {
                    OnErrorOccurred(device.DeviceId, "UDP client mode requires a valid remote endpoint.");
                    return false;
                }

                var remoteEndPoint = new IPEndPoint(settings.RemoteAddress, settings.RemotePort);
                _socket.Connect(remoteEndPoint);

                var connection = new DeviceConnection
                {
                    DeviceId = device.DeviceId,
                    RemoteEndPoint = remoteEndPoint,
                    ConnectTime = DateTime.Now,
                    LastActiveTime = DateTime.Now
                };
                _connections[device.DeviceId] = connection;

                OnDeviceConnected(device.DeviceId, device);
                return true;
            }

            _expectedDevice = CloneDevice(device);
            _expectedRemoteAddress = settings.RemoteAddress;
            _pendingFirstPacketSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var timeoutMs = _configProvider.GetConfig().Global.ConnectionTimeout;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts?.Token ?? CancellationToken.None);
            timeoutCts.CancelAfter(timeoutMs);

            try
            {
                return await _pendingFirstPacketSource.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                OnErrorOccurred(device.DeviceId, $"Timed out waiting for first UDP packet on {_localAddress}:{_localPort}.");
                return false;
            }
            finally
            {
                if (!_connections.ContainsKey(device.DeviceId))
                {
                    _expectedDevice = null;
                    _expectedRemoteAddress = null;
                }

                _pendingFirstPacketSource = null;
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(device.DeviceId, $"Failed to connect UDP device: {ex.Message}", ex);
            return false;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public override Task DisconnectDeviceAsync(string deviceId)
    {
        _connections.TryRemove(deviceId, out _);
        OnDeviceDisconnected(deviceId, "Disconnected.");
        return Task.CompletedTask;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var config = _configProvider.GetConfig();
        var buffer = new byte[config.Udp.ReceiveBufferSize];
        EndPoint remoteTemplate = new IPEndPoint(IPAddress.Any, 0);

        while (!cancellationToken.IsCancellationRequested && _socket != null)
        {
            try
            {
                var receiveResult = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, remoteTemplate, cancellationToken).ConfigureAwait(false);
                if (receiveResult.ReceivedBytes <= 0)
                {
                    continue;
                }

                var remoteEndPoint = receiveResult.RemoteEndPoint as IPEndPoint;
                if (remoteEndPoint == null)
                {
                    continue;
                }

                var data = new byte[receiveResult.ReceivedBytes];
                Buffer.BlockCopy(buffer, 0, data, 0, receiveResult.ReceivedBytes);

                if (_mode == ConnectionMode.Server && TryBindServerConnection(remoteEndPoint))
                {
                    // Bound expected device based on first matching datagram.
                }

                var deviceId = ResolveDeviceId(remoteEndPoint);
                if (string.IsNullOrWhiteSpace(deviceId) || !_connections.TryGetValue(deviceId, out var connection))
                {
                    continue;
                }

                connection.BytesReceived += receiveResult.ReceivedBytes;
                connection.LastActiveTime = DateTime.Now;
                OnDataReceived(deviceId, data);
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
                _logger.Error($"Failed to receive UDP data: {ex.Message}", ex);
            }
        }
    }

    private bool TryBindServerConnection(IPEndPoint remoteEndPoint)
    {
        if (_expectedDevice == null || _connections.ContainsKey(_expectedDevice.DeviceId))
        {
            return false;
        }

        if (_expectedRemoteAddress != null && !Equals(_expectedRemoteAddress, remoteEndPoint.Address))
        {
            _logger.Warning($"Ignored UDP packet from {remoteEndPoint} because it does not match expected address {_expectedRemoteAddress}.");
            return false;
        }

        var connection = new DeviceConnection
        {
            DeviceId = _expectedDevice.DeviceId,
            RemoteEndPoint = remoteEndPoint,
            ConnectTime = DateTime.Now,
            LastActiveTime = DateTime.Now
        };
        _connections[connection.DeviceId] = connection;

        var connectedDevice = CloneDevice(_expectedDevice);
        connectedDevice.IpAddress = remoteEndPoint.Address.ToString();
        connectedDevice.Port = remoteEndPoint.Port;

        _expectedDevice = null;
        _expectedRemoteAddress = null;
        _pendingFirstPacketSource?.TrySetResult(true);
        OnDeviceConnected(connection.DeviceId, connectedDevice);
        _logger.Info($"Bound UDP device {connection.DeviceId} to remote endpoint {remoteEndPoint}.");
        return true;
    }

    private string ResolveDeviceId(IPEndPoint remoteEndPoint)
    {
        foreach (var pair in _connections)
        {
            if (pair.Value.RemoteEndPoint is IPEndPoint knownEndPoint &&
                Equals(knownEndPoint.Address, remoteEndPoint.Address) &&
                knownEndPoint.Port == remoteEndPoint.Port)
            {
                return pair.Key;
            }
        }

        if (_mode == ConnectionMode.Client && _connections.Count == 1)
        {
            return _connections.Keys.First();
        }

        return string.Empty;
    }

    private static DeviceInfo CloneDevice(DeviceInfo device)
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
