namespace Vktun.IoT.Connector.Communication.Mqtt;

public enum MqttQualityOfService
{
    AtMostOnce = 0,
    AtLeastOnce = 1,
    ExactlyOnce = 2
}

public sealed class MqttPublishOptions
{
    public string Topic { get; set; } = string.Empty;
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    public MqttQualityOfService Qos { get; set; } = MqttQualityOfService.AtMostOnce;
    public bool Retain { get; set; }
}

public sealed class MqttSubscribeOptions
{
    public string TopicFilter { get; set; } = string.Empty;
    public MqttQualityOfService Qos { get; set; } = MqttQualityOfService.AtMostOnce;
}

public sealed class MqttReceivedMessage
{
    public string Topic { get; set; } = string.Empty;
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    public MqttQualityOfService Qos { get; set; } = MqttQualityOfService.AtMostOnce;
    public bool Retain { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
