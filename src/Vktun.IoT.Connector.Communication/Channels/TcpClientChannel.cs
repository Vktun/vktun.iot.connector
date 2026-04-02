using System.Net;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Driver.Sockets;

namespace Vktun.IoT.Connector.Communication.Channels;

public class TcpClientChannel : CommunicationChannelBase
{
    private readonly ISocketDriver _socketDriver;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private CancellationTokenSource? _receiveLoopCts;

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
        if (!_isConnected)
        {
            return;
        }

        _isConnected = false;
        _receiveLoopCts?.Cancel();
        _receiveLoopCts?.Dispose();
        _receiveLoopCts = null;

        var deviceIds = _connections.Keys.ToArray();
        foreach (var deviceId in deviceIds)
        {
            await DisconnectDeviceAsync(deviceId).ConfigureAwait(false);
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

        try
        {
            var bytesSent = await _socketDriver.SendAsync(data, cancellationToken).ConfigureAwait(false);
            if (_connections.TryGetValue(deviceId, out var connection))
            {
                connection.BytesSent += bytesSent;
                connection.LastActiveTime = DateTime.Now;
            }

            return bytesSent;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(deviceId, $"Failed to send TCP data: {ex.Message}", ex);
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

            var endPoint = new IPEndPoint(IPAddress.Parse(device.IpAddress), device.Port);
            var connected = await _socketDriver.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
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
            _receiveLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.CancellationTokenSource.Token);
            _ = ReceiveLoopAsync(device.DeviceId, connection.ReceiveBuffer, _receiveLoopCts.Token);
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
        if (_connections.TryRemove(deviceId, out var connection))
        {
            connection.CancellationTokenSource?.Cancel();
            connection.CancellationTokenSource?.Dispose();
            await _socketDriver.DisconnectAsync().ConfigureAwait(false);
            OnDeviceDisconnected(deviceId, "Disconnected");
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
                    await DisconnectDeviceAsync(deviceId).ConfigureAwait(false);
                    break;
                }

                var data = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, data, 0, bytesRead);

                if (_connections.TryGetValue(deviceId, out var connection))
                {
                    connection.BytesReceived += bytesRead;
                    connection.LastActiveTime = DateTime.Now;
                }

                OnDataReceived(deviceId, data);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnErrorOccurred(deviceId, $"Failed to receive TCP data: {ex.Message}", ex);
                await DisconnectDeviceAsync(deviceId).ConfigureAwait(false);
                break;
            }
        }
    }
}
