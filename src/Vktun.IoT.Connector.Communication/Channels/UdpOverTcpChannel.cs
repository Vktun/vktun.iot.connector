using System.Net;
using System.Net.Sockets;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;

namespace Vktun.IoT.Connector.Communication.Channels;

public sealed class UdpOverTcpChannel : CommunicationChannelBase
{
    private const int PrefixLength = 4;
    private const int MaxPayloadLength = 16 * 1024 * 1024;

    private readonly ConnectionMode _mode;
    private readonly IPAddress _localAddress;
    private readonly int _localPort;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _lifetimeCts;
    private CancellationTokenSource? _connectionCts;
    private Task? _acceptLoopTask;
    private Task? _receiveLoopTask;
    private DeviceInfo? _expectedDevice;
    private TaskCompletionSource<bool>? _pendingServerConnection;

    public override CommunicationType CommunicationType => CommunicationType.UdpOverTcp;
    public override ConnectionMode ConnectionMode => _mode;

    public UdpOverTcpChannel(
        ConnectionMode mode,
        string localIpAddress,
        int localPort,
        IConfigurationProvider configProvider,
        ILogger logger)
        : base(configProvider, logger)
    {
        _mode = mode;
        _localAddress = string.IsNullOrWhiteSpace(localIpAddress) ? IPAddress.Any : IPAddress.Parse(localIpAddress);
        _localPort = localPort;
        ChannelId = $"UdpOverTcp_{mode}_{_localAddress}_{localPort}";
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

            if (_mode == ConnectionMode.Server)
            {
                _listener = new TcpListener(_localAddress, _localPort);
                _listener.Start(_configProvider.GetConfig().Tcp.ListenBacklog);
                _acceptLoopTask = AcceptLoopAsync(_lifetimeCts.Token);
                _logger.Info($"UDP-over-TCP server is listening on {_localAddress}:{_localPort}.");
            }

            _isConnected = true;
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to open UDP-over-TCP channel: {ex.Message}", ex);
            return Task.FromResult(false);
        }
    }

    public override async Task CloseAsync()
    {
        if (!_isConnected && _connections.IsEmpty && _client == null && _listener == null)
        {
            return;
        }

        _isConnected = false;
        _pendingServerConnection?.TrySetCanceled();
        _pendingServerConnection = null;
        _expectedDevice = null;

        _connectionCts?.Cancel();
        _lifetimeCts?.Cancel();

        CloseClient();

        try
        {
            _listener?.Stop();
        }
        catch (SocketException)
        {
        }

        _listener = null;

        foreach (var deviceId in _connections.Keys.ToArray())
        {
            if (_connections.TryRemove(deviceId, out _))
            {
                OnDeviceDisconnected(deviceId, "Disconnected.");
            }
        }

        await WaitForTaskAsync(_receiveLoopTask).ConfigureAwait(false);
        await WaitForTaskAsync(_acceptLoopTask).ConfigureAwait(false);

        _connectionCts?.Dispose();
        _connectionCts = null;
        _lifetimeCts?.Dispose();
        _lifetimeCts = null;
        _receiveLoopTask = null;
        _acceptLoopTask = null;
    }

    public override Task<int> SendAsync(string deviceId, byte[] data, CancellationToken cancellationToken = default)
    {
        return SendAsync(deviceId, new ReadOnlyMemory<byte>(data), cancellationToken);
    }

    public override async Task<int> SendAsync(string deviceId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (!_isConnected || !_connections.TryGetValue(deviceId, out var connection) || _stream == null)
        {
            return 0;
        }

        var frame = CreateFrame(data);

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            connection.AddBytesSent(frame.Length);
            connection.LastActiveTime = DateTime.Now;
            OnDataSent(deviceId, data.ToArray(), frame.Length);
            return frame.Length;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            OnErrorOccurred(deviceId, $"Failed to send UDP-over-TCP data: {ex.Message}", ex);
            return 0;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public override async IAsyncEnumerable<ReceivedData> ReceiveAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        throw new NotSupportedException("UdpOverTcpChannel uses event-based data reception (DataReceived event).");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    public override async Task<bool> ConnectDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (_mode == ConnectionMode.Client)
        {
            return await ConnectClientDeviceAsync(device, cancellationToken).ConfigureAwait(false);
        }

        return await WaitForServerDeviceAsync(device, cancellationToken).ConfigureAwait(false);
    }

    public override Task DisconnectDeviceAsync(string deviceId)
    {
        return DisconnectDeviceCoreAsync(deviceId, waitForReceiveLoop: true, "Disconnected.");
    }

    private async Task<bool> ConnectClientDeviceAsync(DeviceInfo device, CancellationToken cancellationToken)
    {
        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connections.ContainsKey(device.DeviceId))
            {
                return true;
            }

            if (!await OpenAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            var validation = ConnectionSettingsValidator.ValidateAndNormalize(device);
            if (!validation.IsValid || validation.Settings?.RemoteAddress == null)
            {
                OnErrorOccurred(device.DeviceId, validation.ErrorMessage);
                return false;
            }

            var settings = validation.Settings;
            var tcpClient = CreateTcpClient();
            if (settings.LocalPort > 0 || !settings.LocalAddress.Equals(IPAddress.Any))
            {
                tcpClient.Client.Bind(new IPEndPoint(settings.LocalAddress, settings.LocalPort));
            }

            try
            {
                await tcpClient.ConnectAsync(settings.RemoteAddress, settings.RemotePort, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                tcpClient.Dispose();
                throw;
            }

            BindConnectedClient(device, tcpClient);
            OnDeviceConnected(device.DeviceId, device);
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(device.DeviceId, $"Failed to connect UDP-over-TCP device: {ex.Message}", ex);
            return false;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task<bool> WaitForServerDeviceAsync(DeviceInfo device, CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool> pendingConnection;

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connections.ContainsKey(device.DeviceId))
            {
                return true;
            }

            if (!await OpenAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            var validation = ConnectionSettingsValidator.ValidateAndNormalize(device);
            if (!validation.IsValid || validation.Settings == null)
            {
                OnErrorOccurred(device.DeviceId, validation.ErrorMessage);
                return false;
            }

            _expectedDevice = CloneDevice(device);
            _pendingServerConnection = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingConnection = _pendingServerConnection;
        }
        finally
        {
            _connectionLock.Release();
        }

        var timeoutMs = _configProvider.GetConfig().Global.ConnectionTimeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts?.Token ?? CancellationToken.None);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            return await pendingConnection.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            OnErrorOccurred(device.DeviceId, $"Timed out waiting for UDP-over-TCP client on {_localAddress}:{_localPort}.");
            return false;
        }
        finally
        {
            if (!_connections.ContainsKey(device.DeviceId))
            {
                _expectedDevice = null;
            }

            if (ReferenceEquals(_pendingServerConnection, pendingConnection))
            {
                _pendingServerConnection = null;
            }
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            TcpClient? acceptedClient = null;
            try
            {
                acceptedClient = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                await BindAcceptedClientAsync(acceptedClient, cancellationToken).ConfigureAwait(false);
                acceptedClient = null;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to accept UDP-over-TCP client: {ex.Message}", ex);
            }
            finally
            {
                acceptedClient?.Dispose();
            }
        }
    }

    private async Task BindAcceptedClientAsync(TcpClient acceptedClient, CancellationToken cancellationToken)
    {
        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_expectedDevice == null || _connections.ContainsKey(_expectedDevice.DeviceId))
            {
                acceptedClient.Dispose();
                return;
            }

            var connectedDevice = CloneDevice(_expectedDevice);
            if (acceptedClient.Client.RemoteEndPoint is IPEndPoint remoteEndPoint)
            {
                connectedDevice.IpAddress = remoteEndPoint.Address.ToString();
                connectedDevice.Port = remoteEndPoint.Port;
            }

            BindConnectedClient(connectedDevice, acceptedClient);
            _expectedDevice = null;
            _pendingServerConnection?.TrySetResult(true);
            OnDeviceConnected(connectedDevice.DeviceId, connectedDevice);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private void BindConnectedClient(DeviceInfo device, TcpClient tcpClient)
    {
        CloseClient();

        _client = tcpClient;
        _stream = tcpClient.GetStream();
        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
        _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts?.Token ?? CancellationToken.None);

        var remoteEndPoint = tcpClient.Client.RemoteEndPoint as IPEndPoint;
        var connection = new DeviceConnection
        {
            DeviceId = device.DeviceId,
            Socket = tcpClient.Client,
            RemoteEndPoint = remoteEndPoint,
            ConnectTime = DateTime.Now,
            LastActiveTime = DateTime.Now
        };

        _connections[device.DeviceId] = connection;
        _receiveLoopTask = ReceiveLoopAsync(device.DeviceId, _stream, _connectionCts.Token);
    }

    private async Task ReceiveLoopAsync(string deviceId, NetworkStream stream, CancellationToken cancellationToken)
    {
        var prefix = new byte[PrefixLength];

        while (!cancellationToken.IsCancellationRequested && _isConnected)
        {
            try
            {
                if (!await ReadExactlyOrDisconnectAsync(stream, prefix, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                var payloadLength = DecodeLength(prefix);
                if (payloadLength < 0 || payloadLength > MaxPayloadLength)
                {
                    OnErrorOccurred(deviceId, $"Invalid UDP-over-TCP payload length: {payloadLength}.");
                    break;
                }

                var payload = new byte[payloadLength];
                if (payloadLength > 0 &&
                    !await ReadExactlyOrDisconnectAsync(stream, payload, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                if (_connections.TryGetValue(deviceId, out var connection))
                {
                    connection.AddBytesReceived(PrefixLength + payloadLength);
                    connection.LastActiveTime = DateTime.Now;
                }

                OnDataReceived(deviceId, payload);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (IOException ex) when (IsReceiveLoopStopping(deviceId, cancellationToken))
            {
                _logger.Debug($"UDP-over-TCP receive loop stopped for device {deviceId}: {ex.Message}");
                break;
            }
            catch (Exception ex)
            {
                OnErrorOccurred(deviceId, $"Failed to receive UDP-over-TCP data: {ex.Message}", ex);
                break;
            }
        }

        if (!IsReceiveLoopStopping(deviceId, cancellationToken))
        {
            await DisconnectDeviceCoreAsync(deviceId, waitForReceiveLoop: false, "Remote disconnected.").ConfigureAwait(false);
        }
    }

    private async Task DisconnectDeviceCoreAsync(string deviceId, bool waitForReceiveLoop, string reason)
    {
        if (_connections.TryRemove(deviceId, out _))
        {
            _connectionCts?.Cancel();
            CloseClient();

            if (waitForReceiveLoop)
            {
                await WaitForTaskAsync(_receiveLoopTask).ConfigureAwait(false);
            }

            _connectionCts?.Dispose();
            _connectionCts = null;
            _receiveLoopTask = null;
            OnDeviceDisconnected(deviceId, reason);
        }
    }

    private TcpClient CreateTcpClient()
    {
        var config = _configProvider.GetConfig();
        return new TcpClient(AddressFamily.InterNetwork)
        {
            NoDelay = config.Tcp.NoDelay,
            ReceiveBufferSize = config.Tcp.ReceiveBufferSize,
            SendBufferSize = config.Tcp.SendBufferSize
        };
    }

    private void CloseClient()
    {
        try
        {
            _stream?.Close();
        }
        catch (IOException)
        {
        }

        _stream?.Dispose();
        _stream = null;

        _client?.Close();
        _client?.Dispose();
        _client = null;
    }

    private bool IsReceiveLoopStopping(string deviceId, CancellationToken cancellationToken)
    {
        return cancellationToken.IsCancellationRequested || !_isConnected || !_connections.ContainsKey(deviceId);
    }

    private static byte[] CreateFrame(ReadOnlyMemory<byte> payload)
    {
        var frame = new byte[payload.Length + PrefixLength];
        frame[0] = (byte)(payload.Length >> 24);
        frame[1] = (byte)(payload.Length >> 16);
        frame[2] = (byte)(payload.Length >> 8);
        frame[3] = (byte)payload.Length;
        payload.CopyTo(frame.AsMemory(PrefixLength));
        return frame;
    }

    private static int DecodeLength(byte[] prefix)
    {
        return (prefix[0] << 24) | (prefix[1] << 16) | (prefix[2] << 8) | prefix[3];
    }

    private static async Task<bool> ReadExactlyOrDisconnectAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static async Task WaitForTaskAsync(Task? task)
    {
        if (task == null || task.IsCompleted)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
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

    public override async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await CloseAsync().ConfigureAwait(false);
        _connectionLock.Dispose();
        _sendLock.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
