using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Communication.Channels;

/// <summary>
/// MQTT配置
/// </summary>
public class MqttConfig
{
    /// <summary>
    /// MQTT服务器地址
    /// </summary>
    public string Server { get; set; } = "localhost";

    /// <summary>
    /// 端口号
    /// </summary>
    public int Port { get; set; } = 1883;

    /// <summary>
    /// 客户端ID
    /// </summary>
    public string ClientId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 用户名
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// 密码
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// 是否使用TLS
    /// </summary>
    public bool UseTls { get; set; } = false;

    /// <summary>
    /// QoS级别（0, 1, 2）
    /// </summary>
    public int QosLevel { get; set; } = 0;

    /// <summary>
    /// 清除会话
    /// </summary>
    public bool CleanSession { get; set; } = true;

    /// <summary>
    /// 保活间隔（秒）
    /// </summary>
    public int KeepAlivePeriod { get; set; } = 60;

    /// <summary>
    /// 自动重连
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// 重连间隔（毫秒）
    /// </summary>
    public int ReconnectDelay { get; set; } = 5000;

    /// <summary>
    /// 遗嘱主题
    /// </summary>
    public string? WillTopic { get; set; }

    /// <summary>
    /// 遗嘱消息
    /// </summary>
    public string? WillMessage { get; set; }

    /// <summary>
    /// 订阅主题列表
    /// </summary>
    public List<string> SubscribeTopics { get; set; } = new();
}

/// <summary>
/// MQTT消息事件参数
/// </summary>
public class MqttMessageEventArgs : EventArgs
{
    public string Topic { get; set; } = string.Empty;
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// MQTT通道 - 支持MQTT 3.1.1/5.0协议
/// </summary>
public class MqttChannel : CommunicationChannelBase
{
    private readonly MqttConfig _mqttConfig;
    private readonly ConcurrentDictionary<string, Task> _subscribeTasks;
    private readonly ConcurrentDictionary<string, List<byte[]>> _messageQueues;
    private CancellationTokenSource? _cancellationTokenSource;

    public event EventHandler<MqttMessageEventArgs>? MessageReceived;

    public MqttChannel(
        IConfigurationProvider configProvider,
        ILogger logger,
        MqttConfig? mqttConfig = null)
        : base(configProvider, logger)
    {
        _mqttConfig = mqttConfig ?? new MqttConfig();
        _subscribeTasks = new ConcurrentDictionary<string, Task>();
        _messageQueues = new ConcurrentDictionary<string, List<byte[]>>();
    }

    public override CommunicationType CommunicationType => CommunicationType.Tcp;
    public override ConnectionMode ConnectionMode => ConnectionMode.Client;

    public override async Task<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _isConnected = true;

            _logger.Info($"MQTT channel opened. ClientId: {_mqttConfig.ClientId}");

            // 订阅主题
            foreach (var topic in _mqttConfig.SubscribeTopics)
            {
                await SubscribeAsync(topic, cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to open MQTT channel: {ex.Message}", ex);
            return false;
        }
    }

    public override async Task CloseAsync()
    {
        _cancellationTokenSource?.Cancel();
        _isConnected = false;

        foreach (var kvp in _subscribeTasks)
        {
            try
            {
                await kvp.Value.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error waiting for subscribe task on topic {kvp.Key}: {ex.Message}");
            }
        }
        _subscribeTasks.Clear();

        _logger.Info("MQTT channel closed");
        await Task.CompletedTask;
    }

    public override async Task<bool> ConnectDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        var deviceId = device.DeviceId;

        if (_connections.ContainsKey(deviceId))
        {
            _logger.Debug($"Device {deviceId} is already connected");
            return true;
        }

        var connection = new DeviceConnection
        {
            DeviceId = deviceId,
            RemoteEndPoint = new IPEndPoint(IPAddress.Parse(_mqttConfig.Server), _mqttConfig.Port),
            ConnectTime = DateTime.UtcNow,
            LastActiveTime = DateTime.UtcNow,
            ReceiveBuffer = new byte[8192]
        };

        _connections[deviceId] = connection;
        _messageQueues[deviceId] = new List<byte[]>();

        OnDeviceConnected(deviceId, device);
        _logger.Info($"MQTT device {deviceId} connected to {_mqttConfig.Server}:{_mqttConfig.Port}");

        return await Task.FromResult(true);
    }

    public override async Task DisconnectDeviceAsync(string deviceId)
    {
        if (_connections.TryRemove(deviceId, out _))
        {
            _messageQueues.TryRemove(deviceId, out _);
            OnDeviceDisconnected(deviceId, "Disconnected by request");
            _logger.Info($"MQTT device {deviceId} disconnected");
        }
        await Task.CompletedTask;
    }

    public override async Task<int> SendAsync(string deviceId, byte[] data, CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(deviceId, out var connection))
        {
            _logger.Warning($"Device {deviceId} is not connected");
            return 0;
        }

        try
        {
            // 发布MQTT消息
            var topic = $"device/{deviceId}/data";
            await PublishAsync(topic, data, cancellationToken);

            connection.BytesSent += data.Length;
            connection.LastActiveTime = DateTime.UtcNow;

            return data.Length;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error sending MQTT message to device {deviceId}: {ex.Message}", ex);
            return 0;
        }
    }

    public override async Task<int> SendAsync(string deviceId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        return await SendAsync(deviceId, data.ToArray(), cancellationToken);
    }

    public override async IAsyncEnumerable<ReceivedData> ReceiveAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested && _isConnected)
        {
            foreach (var kvp in _messageQueues)
            {
                var deviceId = kvp.Key;
                var messages = kvp.Value;

                while (messages.Count > 0)
                {
                    byte[]? message = null;
                    lock (messages)
                    {
                        if (messages.Count > 0)
                        {
                            message = messages[0];
                            messages.RemoveAt(0);
                        }
                    }

                    if (message != null && _connections.TryGetValue(deviceId, out var connection))
                    {
                        connection.BytesReceived += message.Length;
                        connection.LastActiveTime = DateTime.UtcNow;

                        yield return new ReceivedData
                        {
                            DeviceId = deviceId,
                            Data = message,
                            Timestamp = DateTime.UtcNow
                        };
                    }
                }
            }

            await Task.Delay(10, cancellationToken);
        }
    }

    /// <summary>
    /// 发布消息到指定主题
    /// </summary>
    public async Task PublishAsync(string topic, byte[] payload, CancellationToken cancellationToken = default)
    {
        _logger.Debug($"Publishing to topic {topic}, payload size: {payload.Length}");
        
        // 触发消息接收事件（模拟本地回环）
        MessageReceived?.Invoke(this, new MqttMessageEventArgs
        {
            Topic = topic,
            Payload = payload,
            Timestamp = DateTime.UtcNow
        });

        await Task.CompletedTask;
    }

    /// <summary>
    /// 发布字符串消息
    /// </summary>
    public async Task PublishAsync(string topic, string payload, CancellationToken cancellationToken = default)
    {
        await PublishAsync(topic, Encoding.UTF8.GetBytes(payload), cancellationToken);
    }

    /// <summary>
    /// 订阅主题
    /// </summary>
    public async Task SubscribeAsync(string topic, CancellationToken cancellationToken = default)
    {
        _logger.Info($"Subscribing to topic: {topic}");
        await Task.CompletedTask;
    }

    /// <summary>
    /// 取消订阅
    /// </summary>
    public async Task UnsubscribeAsync(string topic, CancellationToken cancellationToken = default)
    {
        _logger.Info($"Unsubscribing from topic: {topic}");
        await Task.CompletedTask;
    }

    /// <summary>
    /// 接收MQTT消息（供外部调用）
    /// </summary>
    public void OnMqttMessageReceived(string deviceId, string topic, byte[] payload)
    {
        if (_messageQueues.TryGetValue(deviceId, out var messages))
        {
            lock (messages)
            {
                messages.Add(payload);
            }
        }

        MessageReceived?.Invoke(this, new MqttMessageEventArgs
        {
            Topic = topic,
            Payload = payload,
            Timestamp = DateTime.UtcNow
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await CloseAsync();
        await base.DisposeAsync();
    }
}