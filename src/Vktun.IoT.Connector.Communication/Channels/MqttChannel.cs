using System.Collections.Concurrent;
using System.Net;
using System.Text;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using Vktun.IoT.Connector.Communication.Mqtt;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Communication.Channels;

public class MqttConfig
{
    public string Server { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string ClientId { get; set; } = Guid.NewGuid().ToString();
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool UseTls { get; set; }
    public int QosLevel { get; set; }
    public bool CleanSession { get; set; } = true;
    public int KeepAlivePeriod { get; set; } = 60;
    public bool AutoReconnect { get; set; } = true;
    public int ReconnectDelay { get; set; } = 5000;
    public string? WillTopic { get; set; }
    public string? WillMessage { get; set; }
    public List<string> SubscribeTopics { get; set; } = new();
    public bool EnableLocalEcho { get; set; }
    public bool UseInMemoryTransport { get; set; }
}

public class MqttMessageEventArgs : EventArgs
{
    public string DeviceId { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    public int QosLevel { get; set; }
    public bool Retain { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class MqttChannel : CommunicationChannelBase
{
    private readonly MqttConfig _mqttConfig;
    private readonly IMqttClient _mqttClient;
    private readonly ConcurrentDictionary<string, List<byte[]>> _messageQueues = new();
    private readonly ConcurrentDictionary<string, MqttSubscribeOptions> _subscriptions = new(StringComparer.Ordinal);
    private CancellationTokenSource? _reconnectCts;
    private MqttClientOptions? _clientOptions;

    public event EventHandler<MqttMessageEventArgs>? MessageReceived;

    public MqttChannel(
        IConfigurationProvider configProvider,
        ILogger logger,
        MqttConfig? mqttConfig = null)
        : base(configProvider, logger)
    {
        _mqttConfig = mqttConfig ?? new MqttConfig();
        _mqttClient = new MqttFactory().CreateMqttClient();
        _mqttClient.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
        _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
    }

    public override CommunicationType CommunicationType => CommunicationType.Mqtt;
    public override ConnectionMode ConnectionMode => ConnectionMode.Client;

    public override async Task<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();
            _reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _clientOptions = BuildClientOptions();

            if (!_mqttConfig.UseInMemoryTransport && !_mqttClient.IsConnected)
            {
                await _mqttClient.ConnectAsync(_clientOptions, cancellationToken).ConfigureAwait(false);
            }

            _isConnected = true;
            _logger.Info($"MQTT connected. ClientId: {_mqttConfig.ClientId}, broker: {_mqttConfig.Server}:{_mqttConfig.Port}");

            foreach (var topic in _mqttConfig.SubscribeTopics)
            {
                await SubscribeAsync(topic, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception ex)
        {
            _isConnected = false;
            _logger.Error($"Failed to open MQTT channel: {ex.Message}", ex);
            return false;
        }
    }

    public override async Task CloseAsync()
    {
        var reconnectCts = _reconnectCts;
        _reconnectCts = null;
        try
        {
            reconnectCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        reconnectCts?.Dispose();

        _isConnected = false;

        if (!_mqttConfig.UseInMemoryTransport && _mqttClient.IsConnected)
        {
            await _mqttClient.DisconnectAsync().ConfigureAwait(false);
        }

        _logger.Info("MQTT channel closed.");
    }

    public override Task<bool> ConnectDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        var deviceId = device.DeviceId;
        if (_connections.ContainsKey(deviceId))
        {
            _logger.Debug($"MQTT device {deviceId} is already connected.");
            return Task.FromResult(true);
        }

        var connection = new DeviceConnection
        {
            DeviceId = deviceId,
            RemoteEndPoint = IPAddress.TryParse(_mqttConfig.Server, out var serverAddress)
                ? new IPEndPoint(serverAddress, _mqttConfig.Port)
                : null,
            ConnectTime = DateTime.UtcNow,
            LastActiveTime = DateTime.UtcNow,
            ReceiveBuffer = new byte[8192]
        };

        _connections[deviceId] = connection;
        _messageQueues[deviceId] = new List<byte[]>();

        OnDeviceConnected(deviceId, device);
        _logger.Info($"MQTT device {deviceId} registered for broker {_mqttConfig.Server}:{_mqttConfig.Port}.");
        return Task.FromResult(true);
    }

    public override Task DisconnectDeviceAsync(string deviceId)
    {
        if (_connections.TryRemove(deviceId, out _))
        {
            _messageQueues.TryRemove(deviceId, out _);
            OnDeviceDisconnected(deviceId, "Disconnected by request");
            _logger.Info($"MQTT device {deviceId} disconnected.");
        }

        return Task.CompletedTask;
    }

    public override Task<int> SendAsync(string deviceId, byte[] data, CancellationToken cancellationToken = default)
    {
        return SendAsync(deviceId, new ReadOnlyMemory<byte>(data), cancellationToken);
    }

    public override async Task<int> SendAsync(string deviceId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(deviceId, out var connection))
        {
            _logger.Warning($"MQTT device {deviceId} is not connected.");
            return 0;
        }

        try
        {
            await PublishAsync(new MqttPublishOptions
            {
                Topic = GetPublishTopic(deviceId),
                Payload = data.ToArray(),
                Qos = ToQualityOfService(_mqttConfig.QosLevel)
            }, cancellationToken).ConfigureAwait(false);

            connection.BytesSent += data.Length;
            connection.LastActiveTime = DateTime.UtcNow;
            OnDataSent(deviceId, data.ToArray(), data.Length);
            return data.Length;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error sending MQTT message to device {deviceId}: {ex.Message}", ex);
            return 0;
        }
    }

    public override async IAsyncEnumerable<ReceivedData> ReceiveAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested && _isConnected)
        {
            foreach (var pair in _messageQueues)
            {
                var deviceId = pair.Key;
                var messages = pair.Value;

                while (true)
                {
                    byte[]? message = null;
                    lock (messages)
                    {
                        if (messages.Count == 0)
                        {
                            break;
                        }

                        message = messages[0];
                        messages.RemoveAt(0);
                    }

                    if (message != null)
                    {
                        yield return new ReceivedData
                        {
                            DeviceId = deviceId,
                            Data = message,
                            Timestamp = DateTime.UtcNow
                        };
                    }
                }
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task PublishAsync(string topic, byte[] payload, CancellationToken cancellationToken = default)
    {
        return PublishAsync(new MqttPublishOptions
        {
            Topic = topic,
            Payload = payload,
            Qos = ToQualityOfService(_mqttConfig.QosLevel)
        }, cancellationToken);
    }

    public Task PublishAsync(string topic, string payload, CancellationToken cancellationToken = default)
    {
        return PublishAsync(topic, Encoding.UTF8.GetBytes(payload), cancellationToken);
    }

    public async Task PublishAsync(MqttPublishOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Topic))
        {
            throw new ArgumentException("MQTT publish topic is required.", nameof(options));
        }

        if (!_mqttConfig.UseInMemoryTransport && !_mqttClient.IsConnected)
        {
            throw new InvalidOperationException("MQTT client is not connected.");
        }

        if (!_mqttConfig.UseInMemoryTransport)
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(options.Topic)
                .WithPayload(options.Payload)
                .WithQualityOfServiceLevel(ToMqttNetQualityOfService(options.Qos))
                .WithRetainFlag(options.Retain)
                .Build();

            await _mqttClient.PublishAsync(message, cancellationToken).ConfigureAwait(false);
        }

        _logger.Debug($"Published MQTT message. Topic: {options.Topic}, payload size: {options.Payload.Length}, qos: {(int)options.Qos}, retain: {options.Retain}");

        if (_mqttConfig.EnableLocalEcho || _mqttConfig.UseInMemoryTransport)
        {
            HandleReceivedMessage(string.Empty, options.Topic, options.Payload, (int)options.Qos, options.Retain);
        }
    }

    public Task SubscribeAsync(string topic, CancellationToken cancellationToken = default)
    {
        return SubscribeAsync(new MqttSubscribeOptions
        {
            TopicFilter = topic,
            Qos = ToQualityOfService(_mqttConfig.QosLevel)
        }, cancellationToken);
    }

    public async Task SubscribeAsync(MqttSubscribeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.TopicFilter))
        {
            throw new ArgumentException("MQTT subscribe topic filter is required.", nameof(options));
        }

        if (!_mqttConfig.UseInMemoryTransport && !_mqttClient.IsConnected)
        {
            throw new InvalidOperationException("MQTT client is not connected.");
        }

        if (!_mqttConfig.UseInMemoryTransport)
        {
            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(filter => filter
                    .WithTopic(options.TopicFilter)
                    .WithQualityOfServiceLevel(ToMqttNetQualityOfService(options.Qos)))
                .Build();

            await _mqttClient.SubscribeAsync(subscribeOptions, cancellationToken).ConfigureAwait(false);
        }

        _subscriptions[options.TopicFilter] = options;
        _logger.Info($"Subscribed MQTT topic filter: {options.TopicFilter}, qos: {(int)options.Qos}");
    }

    public async Task UnsubscribeAsync(string topic, CancellationToken cancellationToken = default)
    {
        if (_mqttConfig.UseInMemoryTransport || !_mqttClient.IsConnected)
        {
            _subscriptions.TryRemove(topic, out _);
            return;
        }

        var unsubscribeOptions = new MqttClientUnsubscribeOptionsBuilder()
            .WithTopicFilter(topic)
            .Build();

        await _mqttClient.UnsubscribeAsync(unsubscribeOptions, cancellationToken).ConfigureAwait(false);
        _subscriptions.TryRemove(topic, out _);
        _logger.Info($"Unsubscribed MQTT topic filter: {topic}");
    }

    public void OnMqttMessageReceived(string deviceId, string topic, byte[] payload)
    {
        HandleReceivedMessage(deviceId, topic, payload, _mqttConfig.QosLevel, retain: false);
    }

    public override async ValueTask DisposeAsync()
    {
        _mqttClient.ApplicationMessageReceivedAsync -= OnApplicationMessageReceivedAsync;
        _mqttClient.DisconnectedAsync -= OnDisconnectedAsync;
        await CloseAsync().ConfigureAwait(false);
        _reconnectCts?.Dispose();
        _reconnectCts = null;
        _mqttClient.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }

    private MqttClientOptions BuildClientOptions()
    {
        var builder = new MqttClientOptionsBuilder()
            .WithClientId(_mqttConfig.ClientId)
            .WithTcpServer(_mqttConfig.Server, _mqttConfig.Port)
            .WithCleanSession(_mqttConfig.CleanSession)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(Math.Max(1, _mqttConfig.KeepAlivePeriod)));

        if (!string.IsNullOrWhiteSpace(_mqttConfig.Username))
        {
            builder.WithCredentials(_mqttConfig.Username, _mqttConfig.Password);
        }

        if (_mqttConfig.UseTls)
        {
            builder.WithTlsOptions(options => options.UseTls());
        }

        if (!string.IsNullOrWhiteSpace(_mqttConfig.WillTopic))
        {
            var willPayload = string.IsNullOrWhiteSpace(_mqttConfig.WillMessage)
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(_mqttConfig.WillMessage);

            builder.WithWillTopic(_mqttConfig.WillTopic)
                .WithWillPayload(willPayload)
                .WithWillQualityOfServiceLevel(ToMqttNetQualityOfService(ToQualityOfService(_mqttConfig.QosLevel)));
        }

        return builder.Build();
    }

    private Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        HandleReceivedMessage(
            ResolveDeviceId(args.ApplicationMessage.Topic),
            args.ApplicationMessage.Topic,
            args.ApplicationMessage.PayloadSegment.ToArray(),
            (int)args.ApplicationMessage.QualityOfServiceLevel,
            args.ApplicationMessage.Retain);

        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        _isConnected = false;
        _logger.Warning($"MQTT disconnected. Reason: {args.Reason}");

        if (!_mqttConfig.AutoReconnect || _reconnectCts == null || _reconnectCts.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        _ = ReconnectAsync(_reconnectCts.Token);
        return Task.CompletedTask;
    }

    private async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !_mqttClient.IsConnected)
        {
            try
            {
                await Task.Delay(Math.Max(100, _mqttConfig.ReconnectDelay), cancellationToken).ConfigureAwait(false);
                if (_clientOptions == null)
                {
                    return;
                }

                await _mqttClient.ConnectAsync(_clientOptions, cancellationToken).ConfigureAwait(false);
                _isConnected = true;
                _logger.Info("MQTT reconnected.");

                foreach (var subscription in _subscriptions.Values)
                {
                    await SubscribeAsync(subscription, cancellationToken).ConfigureAwait(false);
                }

                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.Warning($"MQTT reconnect failed: {ex.Message}");
            }
        }
    }

    private void HandleReceivedMessage(string deviceId, string topic, byte[] payload, int qosLevel, bool retain)
    {
        var resolvedDeviceId = string.IsNullOrWhiteSpace(deviceId) ? ResolveDeviceId(topic) : deviceId;
        if (!string.IsNullOrWhiteSpace(resolvedDeviceId) && _messageQueues.TryGetValue(resolvedDeviceId, out var messages))
        {
            lock (messages)
            {
                messages.Add(payload);
            }

            if (_connections.TryGetValue(resolvedDeviceId, out var connection))
            {
                connection.BytesReceived += payload.Length;
                connection.LastActiveTime = DateTime.UtcNow;
            }
        }

        MessageReceived?.Invoke(this, new MqttMessageEventArgs
        {
            DeviceId = resolvedDeviceId,
            Topic = topic,
            Payload = payload,
            QosLevel = qosLevel,
            Retain = retain,
            Timestamp = DateTime.UtcNow
        });

        if (!string.IsNullOrWhiteSpace(resolvedDeviceId))
        {
            OnDataReceived(resolvedDeviceId, payload);
        }
    }

    private string ResolveDeviceId(string topic)
    {
        foreach (var deviceId in _connections.Keys)
        {
            if (topic.Contains(deviceId, StringComparison.OrdinalIgnoreCase))
            {
                return deviceId;
            }
        }

        return _connections.Keys.FirstOrDefault() ?? string.Empty;
    }

    private static string GetPublishTopic(string deviceId)
    {
        return $"device/{deviceId}/data";
    }

    private static MqttQualityOfService ToQualityOfService(int qosLevel)
    {
        return qosLevel switch
        {
            1 => MqttQualityOfService.AtLeastOnce,
            2 => MqttQualityOfService.ExactlyOnce,
            _ => MqttQualityOfService.AtMostOnce
        };
    }

    private static MqttQualityOfServiceLevel ToMqttNetQualityOfService(MqttQualityOfService qos)
    {
        return qos switch
        {
            MqttQualityOfService.AtLeastOnce => MqttQualityOfServiceLevel.AtLeastOnce,
            MqttQualityOfService.ExactlyOnce => MqttQualityOfServiceLevel.ExactlyOnce,
            _ => MqttQualityOfServiceLevel.AtMostOnce
        };
    }
}
