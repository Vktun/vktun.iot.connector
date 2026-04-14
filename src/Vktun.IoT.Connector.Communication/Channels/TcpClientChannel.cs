using System.Collections.Concurrent;
using System.Net;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;
using Vktun.IoT.Connector.Driver.Sockets;

namespace Vktun.IoT.Connector.Communication.Channels;

public class TcpClientChannel : CommunicationChannelBase
{
    private readonly ISocketDriver _socketDriver;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pendingResponses = new();
    private CancellationTokenSource? _receiveLoopCts;
    private Task _receiveLoopTask = Task.CompletedTask;

    public override CommunicationType CommunicationType => CommunicationType.Tcp;
    public override ConnectionMode ConnectionMode => ConnectionMode.Client;

    public TcpClientChannel(IConfigurationProvider configProvider, ILogger logger)
        : base(configProvider, logger)
    {
        _socketDriver = new TcpSocketDriver(configProvider, logger);
        ChannelId = "TcpClient";
    }

    public override Task<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        _isConnected = true;
        return Task.FromResult(true);
    }

    public override async Task CloseAsync()
    {
        if (!_isConnected && _connections.IsEmpty)
        {
            return;
        }

        _isConnected = false;
        await StopReceiveLoopAsync().ConfigureAwait(false);

        foreach (var pending in _pendingResponses.Values)
        {
            pending.TrySetCanceled();
        }
        _pendingResponses.Clear();

        var deviceIds = _connections.Keys.ToArray();
        foreach (var deviceId in deviceIds)
        {
            await DisconnectDeviceCoreAsync(deviceId, waitForReceiveLoop: false, "Disconnected").ConfigureAwait(false);
        }

        await _socketDriver.DisconnectAsync().ConfigureAwait(false);
    }

    public override Task<int> SendAsync(string deviceId, byte[] data, CancellationToken cancellationToken = default)
    {
        return SendAsync(deviceId, new ReadOnlyMemory<byte>(data), cancellationToken);
    }

    public override async Task<int> SendAsync(string deviceId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (!_isConnected || !_connections.ContainsKey(deviceId))
        {
            return 0;
        }

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bytesSent = await _socketDriver.SendAsync(data, cancellationToken).ConfigureAwait(false);
            if (_connections.TryGetValue(deviceId, out var connection))
            {
                connection.BytesSent += bytesSent;
                connection.LastActiveTime = DateTime.Now;
            }

            OnDataSent(deviceId, data.ToArray(), bytesSent);
            return bytesSent;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(deviceId, $"Failed to send TCP data: {ex.Message}", ex);
            return 0;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<byte[]> SendAndReceiveAsync(string deviceId, byte[] data, int timeoutMs, CancellationToken cancellationToken = default)
    {
        var responseSource = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingResponses[deviceId] = responseSource;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            var sent = await SendAsync(deviceId, data, timeoutCts.Token).ConfigureAwait(false);
            if (sent <= 0)
            {
                throw new InvalidOperationException($"Failed to send data to device {deviceId}.");
            }

            return await responseSource.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        finally
        {
            _pendingResponses.TryRemove(deviceId, out _);
        }
    }

    public override async IAsyncEnumerable<ReceivedData> ReceiveAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        throw new NotSupportedException("TcpClientChannel uses event-based data reception (DataReceived event). Use OnDataReceived instead of ReceiveAsync.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    public override async Task<bool> ConnectDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connections.ContainsKey(device.DeviceId))
            {
                return true;
            }

            await OpenAsync(cancellationToken).ConfigureAwait(false);

            var settings = ConnectionSettingsValidator.ValidateAndNormalize(device).Settings;
            if (settings?.RemoteAddress == null)
            {
                OnErrorOccurred(device.DeviceId, "TCP client mode requires a valid remote endpoint.");
                return false;
            }

            var endPoint = new IPEndPoint(settings.RemoteAddress, settings.RemotePort);
            IPEndPoint? localEndPoint = null;
            if (settings.LocalPort > 0 || !settings.LocalAddress.Equals(IPAddress.Any))
            {
                localEndPoint = new IPEndPoint(settings.LocalAddress, settings.LocalPort);
            }

            var connected = await _socketDriver.ConnectAsync(endPoint, localEndPoint, cancellationToken).ConfigureAwait(false);
            if (!connected)
            {
                return false;
            }

            var config = _configProvider.GetConfig();
            var connection = new DeviceConnection
            {
                DeviceId = device.DeviceId,
                RemoteEndPoint = endPoint,
                ConnectTime = DateTime.Now,
                LastActiveTime = DateTime.Now,
                ReceiveBuffer = new byte[config.Tcp.ReceiveBufferSize],
                CancellationTokenSource = new CancellationTokenSource()
            };

            _connections[device.DeviceId] = connection;
            ChannelId = localEndPoint == null
                ? "TcpClient"
                : $"TcpClient_{localEndPoint.Address}_{localEndPoint.Port}";
            _receiveLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.CancellationTokenSource.Token);
            _receiveLoopTask = ReceiveLoopAsync(device.DeviceId, connection.ReceiveBuffer, _receiveLoopCts.Token);
            OnDeviceConnected(device.DeviceId, device);
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(device.DeviceId, $"Failed to connect TCP device: {ex.Message}", ex);
            return false;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public override async Task DisconnectDeviceAsync(string deviceId)
    {
        await DisconnectDeviceCoreAsync(deviceId, waitForReceiveLoop: true, "Disconnected").ConfigureAwait(false);
    }

    private async Task DisconnectDeviceCoreAsync(string deviceId, bool waitForReceiveLoop, string reason)
    {
        if (_connections.TryRemove(deviceId, out var connection))
        {
            connection.CancellationTokenSource?.Cancel();

            if (waitForReceiveLoop)
            {
                await StopReceiveLoopAsync().ConfigureAwait(false);
            }
            else
            {
                StopReceiveLoopWithoutWaiting();
            }

            connection.CancellationTokenSource?.Dispose();

            if (_pendingResponses.TryRemove(deviceId, out var pending))
            {
                pending.TrySetException(new InvalidOperationException($"Device {deviceId} disconnected."));
            }

            await _socketDriver.DisconnectAsync().ConfigureAwait(false);
            OnDeviceDisconnected(deviceId, reason);
        }
    }

    private async Task ReceiveLoopAsync(string deviceId, byte[] buffer, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _isConnected)
        {
            try
            {
                var bytesRead = await _socketDriver.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (bytesRead <= 0)
                {
                    if (IsReceiveLoopStopping(deviceId, cancellationToken))
                    {
                        _logger.Debug($"TCP receive loop stopped for device {deviceId}.");
                        break;
                    }

                    _logger.Warning($"TCP remote endpoint closed the connection for device {deviceId}.");
                    await DisconnectDeviceCoreAsync(deviceId, waitForReceiveLoop: false, "Remote disconnected").ConfigureAwait(false);
                    break;
                }

                var data = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, data, 0, bytesRead);

                if (_connections.TryGetValue(deviceId, out var connection))
                {
                    connection.BytesReceived += bytesRead;
                    connection.LastActiveTime = DateTime.Now;
                }

                if (_pendingResponses.TryRemove(deviceId, out var pending))
                {
                    pending.TrySetResult(data);
                }

                OnDataReceived(deviceId, data);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException) when (IsReceiveLoopStopping(deviceId, cancellationToken))
            {
                _logger.Debug($"TCP receive loop stopped because the socket was disposed for device {deviceId}.");
                break;
            }
            catch (Exception ex)
            {
                OnErrorOccurred(deviceId, $"Failed to receive TCP data: {ex.Message}", ex);
                await DisconnectDeviceCoreAsync(deviceId, waitForReceiveLoop: false, "Receive failed").ConfigureAwait(false);
                break;
            }
        }
    }

    private async Task StopReceiveLoopAsync()
    {
        var receiveLoopCts = _receiveLoopCts;
        receiveLoopCts?.Cancel();

        var receiveLoopTask = _receiveLoopTask;
        if (!receiveLoopTask.IsCompleted)
        {
            try
            {
                await receiveLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (ReferenceEquals(_receiveLoopCts, receiveLoopCts))
        {
            _receiveLoopCts = null;
        }

        receiveLoopCts?.Dispose();
    }

    private void StopReceiveLoopWithoutWaiting()
    {
        var receiveLoopCts = _receiveLoopCts;
        receiveLoopCts?.Cancel();

        if (ReferenceEquals(_receiveLoopCts, receiveLoopCts))
        {
            _receiveLoopCts = null;
        }

        receiveLoopCts?.Dispose();
    }

    private bool IsReceiveLoopStopping(string deviceId, CancellationToken cancellationToken)
    {
        return cancellationToken.IsCancellationRequested || !_isConnected || !_connections.ContainsKey(deviceId);
    }

    public override async ValueTask DisposeAsync()
    {
        await CloseAsync();

        if (_socketDriver is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (_socketDriver is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _connectionLock.Dispose();
        _sendLock.Dispose();
        await base.DisposeAsync();
    }
}
