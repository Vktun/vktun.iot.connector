using System.Collections.Concurrent;
using System.Text;
using Vktun.IoT.Connector.Communication.Channels;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Communication.Mqtt;

public sealed class MqttMessagingClient : IMqttMessagingClient
{
    private readonly MqttChannel _channel;
    private readonly ILogger _logger;
    private readonly string _clientDeviceId;
    private readonly ConcurrentDictionary<string, Func<MqttReceivedMessage, CancellationToken, Task>> _handlers = new(StringComparer.Ordinal);
    private bool _isDisposed;

    public MqttMessagingClient(IConfigurationProvider configProvider, ILogger logger, MqttConfig? mqttConfig = null)
        : this(new MqttChannel(configProvider, logger, mqttConfig), logger, mqttConfig?.ClientId)
    {
    }

    internal MqttMessagingClient(MqttChannel channel, ILogger logger, string? clientDeviceId = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clientDeviceId = string.IsNullOrWhiteSpace(clientDeviceId) ? "mqtt-client" : clientDeviceId;
        _channel.MessageReceived += OnMessageReceived;
    }

    public bool IsConnected => _channel.IsConnected;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!await _channel.OpenAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Failed to open MQTT channel.");
        }

        var connected = await _channel.ConnectDeviceAsync(new DeviceInfo
        {
            DeviceId = _clientDeviceId,
            CommunicationType = CommunicationType.Mqtt,
            ConnectionMode = ConnectionMode.Client,
            ProtocolType = ProtocolType.Mqtt
        }, cancellationToken).ConfigureAwait(false);

        if (!connected)
        {
            throw new InvalidOperationException($"Failed to connect MQTT client {_clientDeviceId}.");
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _handlers.Clear();
        await _channel.DisconnectDeviceAsync(_clientDeviceId).ConfigureAwait(false);
        await _channel.CloseAsync().ConfigureAwait(false);
    }

    public async Task PublishAsync(MqttPublishOptions options, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Topic))
        {
            throw new ArgumentException("MQTT publish topic is required.", nameof(options));
        }

        await _channel.PublishAsync(options, cancellationToken).ConfigureAwait(false);
    }

    public Task PublishStringAsync(
        string topic,
        string payload,
        MqttQualityOfService qos = MqttQualityOfService.AtMostOnce,
        bool retain = false,
        CancellationToken cancellationToken = default)
    {
        return PublishAsync(new MqttPublishOptions
        {
            Topic = topic,
            Payload = Encoding.UTF8.GetBytes(payload),
            Qos = qos,
            Retain = retain
        }, cancellationToken);
    }

    public async Task SubscribeAsync(
        MqttSubscribeOptions options,
        Func<MqttReceivedMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handler);

        if (string.IsNullOrWhiteSpace(options.TopicFilter))
        {
            throw new ArgumentException("MQTT subscribe topic filter is required.", nameof(options));
        }

        _handlers[options.TopicFilter] = handler;
        await _channel.SubscribeAsync(options, cancellationToken).ConfigureAwait(false);
    }

    public async Task UnsubscribeAsync(string topicFilter, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        _handlers.TryRemove(topicFilter, out _);
        await _channel.UnsubscribeAsync(topicFilter, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _channel.MessageReceived -= OnMessageReceived;
        await DisconnectAsync().ConfigureAwait(false);
        await _channel.DisposeAsync().ConfigureAwait(false);
        _isDisposed = true;
    }

    private void OnMessageReceived(object? sender, MqttMessageEventArgs args)
    {
        foreach (var pair in _handlers)
        {
            if (!MqttTopicFilter.IsMatch(pair.Key, args.Topic))
            {
                continue;
            }

            var message = new MqttReceivedMessage
            {
                Topic = args.Topic,
                Payload = args.Payload,
                Qos = (MqttQualityOfService)args.QosLevel,
                Retain = args.Retain,
                Timestamp = args.Timestamp
            };

            _ = DispatchAsync(pair.Key, pair.Value, message);
        }
    }

    private async Task DispatchAsync(
        string topicFilter,
        Func<MqttReceivedMessage, CancellationToken, Task> handler,
        MqttReceivedMessage message)
    {
        try
        {
            await handler(message, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"MQTT handler for topic filter {topicFilter} failed: {ex.Message}", ex);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
