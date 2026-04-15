namespace Vktun.IoT.Connector.Communication.Mqtt;

public interface IMqttMessagingClient : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task PublishAsync(MqttPublishOptions options, CancellationToken cancellationToken = default);
    Task PublishStringAsync(string topic, string payload, MqttQualityOfService qos = MqttQualityOfService.AtMostOnce, bool retain = false, CancellationToken cancellationToken = default);
    Task SubscribeAsync(MqttSubscribeOptions options, Func<MqttReceivedMessage, CancellationToken, Task> handler, CancellationToken cancellationToken = default);
    Task UnsubscribeAsync(string topicFilter, CancellationToken cancellationToken = default);
}
