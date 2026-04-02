using System.Collections.Concurrent;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Serial.Drivers;
using DriverParity = Vktun.IoT.Connector.Serial.Drivers.Parity;
using DriverStopBits = Vktun.IoT.Connector.Serial.Drivers.StopBits;

namespace Vktun.IoT.Connector.Serial.Channels;

public class SerialChannel : SerialChannelBase
{
    private readonly string _portName;
    private readonly int _baudRate;
    private readonly int _dataBits;
    private readonly Parity _parity;
    private readonly StopBits _stopBits;
    private readonly ConcurrentQueue<byte[]> _receiveQueue = new();
    private readonly ISerialPortDriver _serialPortDriver;
    private CancellationTokenSource? _receiveLoopCts;

    public override CommunicationType CommunicationType => CommunicationType.Serial;
    public override ConnectionMode ConnectionMode => ConnectionMode.Client;

    public SerialChannel(
        string portName,
        int baudRate,
        IConfigurationProvider configProvider,
        ILogger logger,
        int dataBits = 8,
        Parity parity = Parity.None,
        StopBits stopBits = StopBits.One)
        : base(configProvider, logger)
    {
        _portName = portName;
        _baudRate = baudRate;
        _dataBits = dataBits;
        _parity = parity;
        _stopBits = stopBits;
        _serialPortDriver = new SerialPortDriver(
            portName,
            baudRate,
            configProvider,
            logger,
            dataBits,
            MapParity(parity),
            MapStopBits(stopBits));

        ChannelId = $"Serial_{portName}";
    }

    public override async Task<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (_isConnected)
        {
            return true;
        }

        try
        {
            var opened = await _serialPortDriver.OpenAsync().ConfigureAwait(false);
            if (!opened)
            {
                return false;
            }

            _isConnected = true;
            _receiveLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = ReceiveLoopAsync(_receiveLoopCts.Token);
            _logger.Info($"Serial channel opened on {_portName} @ {_baudRate}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to open serial channel {_portName}: {ex.Message}", ex);
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
        _receiveLoopCts?.Cancel();
        _receiveLoopCts?.Dispose();
        _receiveLoopCts = null;

        var deviceIds = _connections.Keys.ToArray();
        foreach (var deviceId in deviceIds)
        {
            await DisconnectDeviceAsync(deviceId).ConfigureAwait(false);
        }

        await _serialPortDriver.CloseAsync().ConfigureAwait(false);
        while (_receiveQueue.TryDequeue(out _))
        {
        }
    }

    public override Task<int> SendAsync(string deviceId, byte[] data, CancellationToken cancellationToken = default)
    {
        return SendAsync(deviceId, new ReadOnlyMemory<byte>(data), cancellationToken);
    }

    public override async Task<int> SendAsync(string deviceId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return 0;
        }

        try
        {
            var buffer = data.ToArray();
            var bytesWritten = await _serialPortDriver.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            if (_connections.TryGetValue(deviceId, out var connection))
            {
                connection.BytesSent += bytesWritten;
                connection.LastActiveTime = DateTime.Now;
            }

            return bytesWritten;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(deviceId, $"Failed to send serial data: {ex.Message}", ex);
            return 0;
        }
    }

    public override async IAsyncEnumerable<ReceivedData> ReceiveAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested && _isConnected)
        {
            if (_receiveQueue.TryDequeue(out var data))
            {
                yield return new ReceivedData
                {
                    DeviceId = _connections.Keys.FirstOrDefault() ?? "Serial",
                    Data = data,
                    Timestamp = DateTime.Now
                };
            }
            else
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public override async Task<bool> ConnectDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        if (!_isConnected && !await OpenAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        _connections[device.DeviceId] = new DeviceConnection
        {
            DeviceId = device.DeviceId,
            ConnectTime = DateTime.Now,
            LastActiveTime = DateTime.Now,
            ReceiveBuffer = new byte[_configProvider.GetConfig().Global.BufferSize],
            CancellationTokenSource = new CancellationTokenSource()
        };

        OnDeviceConnected(device.DeviceId, device);
        return true;
    }

    public override Task DisconnectDeviceAsync(string deviceId)
    {
        if (_connections.TryRemove(deviceId, out var connection))
        {
            connection.CancellationTokenSource?.Cancel();
            connection.CancellationTokenSource?.Dispose();
            OnDeviceDisconnected(deviceId, "Disconnected");
        }

        return Task.CompletedTask;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[_configProvider.GetConfig().Global.BufferSize];

        while (!cancellationToken.IsCancellationRequested && _isConnected)
        {
            try
            {
                var bytesRead = await _serialPortDriver.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (bytesRead <= 0)
                {
                    await Task.Delay(_configProvider.GetConfig().Serial.ReceivePollingInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var data = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, data, 0, bytesRead);
                _receiveQueue.Enqueue(data);

                foreach (var connection in _connections.Values)
                {
                    connection.BytesReceived += bytesRead;
                    connection.LastActiveTime = DateTime.Now;
                    OnDataReceived(connection.DeviceId, data);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnErrorOccurred(string.Empty, $"Failed to receive serial data: {ex.Message}", ex);
                await Task.Delay(_configProvider.GetConfig().Serial.ReceivePollingInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static DriverParity MapParity(Parity parity)
    {
        return parity switch
        {
            Parity.Odd => DriverParity.Odd,
            Parity.Even => DriverParity.Even,
            Parity.Mark => DriverParity.Mark,
            Parity.Space => DriverParity.Space,
            _ => DriverParity.None
        };
    }

    private static DriverStopBits MapStopBits(StopBits stopBits)
    {
        return stopBits switch
        {
            StopBits.OnePointFive => DriverStopBits.OnePointFive,
            StopBits.Two => DriverStopBits.Two,
            _ => DriverStopBits.One
        };
    }
}

public enum Parity
{
    None,
    Odd,
    Even,
    Mark,
    Space
}

public enum StopBits
{
    One,
    OnePointFive,
    Two
}
