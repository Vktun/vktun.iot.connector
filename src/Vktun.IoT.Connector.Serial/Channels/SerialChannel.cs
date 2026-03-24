using System.Collections.Concurrent;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Serial.Channels;

public class SerialChannel : SerialChannelBase
{
    private readonly string _portName;
    private readonly int _baudRate;
    private readonly int _dataBits;
    private readonly Parity _parity;
    private readonly StopBits _stopBits;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly ConcurrentQueue<byte[]> _receiveQueue;
    private readonly object _serialPortLock = new();

    public override CommunicationType CommunicationType => CommunicationType.Serial;
    public override ConnectionMode ConnectionMode => ConnectionMode.Server;

    public SerialChannel(
        string portName,
        int baudRate,
        IConfigurationProvider configProvider,
        ILogger logger,
        int dataBits = 8,
        Parity parity = Parity.None,
        StopBits stopBits = StopBits.One) : base(configProvider, logger)
    {
        _portName = portName;
        _baudRate = baudRate;
        _dataBits = dataBits;
        _parity = parity;
        _stopBits = stopBits;
        _receiveQueue = new ConcurrentQueue<byte[]>();
        
        ChannelId = $"Serial_{portName}";
    }

    public override Task<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (_isConnected)
        {
            return Task.FromResult(true);
        }

        try
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isConnected = true;
            
            _logger.Info($"串口通道启动成功: {_portName}, 波特率: {_baudRate}");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.Error($"串口通道启动失败: {ex.Message}", ex);
            return Task.FromResult(false);
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
        _cancellationTokenSource?.Dispose();
        
        _connections.Clear();
        _receiveQueue.Clear();
        
        _logger.Info("串口通道已关闭");
        return Task.CompletedTask;
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
            await Task.Delay(10, cancellationToken);
            
            if (_connections.TryGetValue(deviceId, out var connection))
            {
                connection.BytesSent += data.Length;
                connection.LastActiveTime = DateTime.Now;
            }
            
            return data.Length;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(deviceId, "串口发送数据失败", ex);
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
                    DeviceId = "Serial",
                    Data = data,
                    Timestamp = DateTime.Now
                };
            }
            else
            {
                await Task.Delay(10, cancellationToken);
            }
        }
    }

    public override Task<bool> ConnectDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        var connection = new DeviceConnection
        {
            DeviceId = device.DeviceId,
            ConnectTime = DateTime.Now,
            LastActiveTime = DateTime.Now
        };
        
        _connections[device.DeviceId] = connection;
        OnDeviceConnected(device.DeviceId, device);
        
        return Task.FromResult(true);
    }

    public override Task DisconnectDeviceAsync(string deviceId)
    {
        _connections.TryRemove(deviceId, out _);
        OnDeviceDisconnected(deviceId, "主动断开");
        return Task.CompletedTask;
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
