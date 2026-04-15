# HTTP/MQTT 通道 NuGet 使用指南

本文档面向 NuGet 使用者，说明如何只安装主包或按通道注册 HTTP/MQTT 能力。

## 推荐安装

外部项目优先安装主入口门面包：

```bash
dotnet add package Vktun.IoT.Connector
```

主包会透传运行时所需实现程序集。只有在做底层扩展或拆包引用时，才单独安装子包。

## 注册 HTTP 通道

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddVktunHttpChannel(options =>
{
    options.ConfigureSdk = config =>
    {
        config.Http.DefaultScheme = "http";
        config.Http.DefaultMethod = "POST";
        config.Http.DefaultContentType = "application/octet-stream";
        config.Http.RequestTimeout = 5000;
        config.Http.MaxConnectionsPerServer = 512;
        config.Http.PooledConnectionLifetimeSeconds = 300;
    };
});
```

HTTP 设备配置字段来自 `DeviceInfo` 与 `ExtendedProperties`：

| 字段 | 来源 | 说明 |
| --- | --- | --- |
| `CommunicationType` | `DeviceInfo.CommunicationType` | 必须为 `Http` |
| `ConnectionMode` | `DeviceInfo.ConnectionMode` | 必须为 `Client` |
| `Url` / `RequestUri` / `EndpointUrl` | `ExtendedProperties` | 完整请求地址，优先级最高 |
| `BaseAddress` / `BaseUrl` | `ExtendedProperties` | 基础地址，可与 `Path` 组合 |
| `Path` / `EndpointPath` | `ExtendedProperties` | 相对路径 |
| `Scheme` | `ExtendedProperties` | 未配置完整地址时使用，默认来自 `Http.DefaultScheme` |
| `IpAddress` / `Port` | `DeviceInfo` | 未配置 URL 时用于构造地址 |
| `Method` / `HttpMethod` | `ExtendedProperties` | 默认来自 `Http.DefaultMethod` |
| `ContentType` | `ExtendedProperties` | 默认来自 `Http.DefaultContentType` |
| `Headers` | `ExtendedProperties` | `Dictionary<string,string>` 或 JSON 对象 |

最小设备示例：

```csharp
var device = new DeviceInfo
{
    DeviceId = "http-device-001",
    CommunicationType = CommunicationType.Http,
    ConnectionMode = ConnectionMode.Client,
    ProtocolType = ProtocolType.Http,
    ExtendedProperties =
    {
        ["BaseUrl"] = "http://127.0.0.1:8080",
        ["Path"] = "/api/telemetry",
        ["Method"] = "POST",
        ["ContentType"] = "application/json",
        ["Headers"] = new Dictionary<string, string>
        {
            ["X-Gateway"] = "gateway-001"
        }
    }
};
```

## 注册 MQTT 通道

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddVktunMqttChannel(mqtt =>
{
    mqtt.Server = "127.0.0.1";
    mqtt.Port = 1883;
    mqtt.ClientId = "gateway-001";
    mqtt.AutoReconnect = true;
});
```

MQTT 设备配置字段：

| 字段 | 来源 | 说明 |
| --- | --- | --- |
| `CommunicationType` | `DeviceInfo.CommunicationType` | 必须为 `Mqtt` |
| `ConnectionMode` | `DeviceInfo.ConnectionMode` | 必须为 `Client` |
| `Server` / `Host` / `BrokerHost` | `ExtendedProperties` | Broker 地址，未配置时使用 `IpAddress` |
| `Port` / `BrokerPort` | `ExtendedProperties` 或 `DeviceInfo.Port` | 默认 `1883` |
| `ClientId` | `ExtendedProperties` | 默认使用 `DeviceId` |
| `Username` / `Password` | `ExtendedProperties` | 认证信息 |
| `UseTls` | `ExtendedProperties` | 是否启用 TLS |
| `Qos` / `QosLevel` | `ExtendedProperties` | 默认 `0` |
| `CleanSession` | `ExtendedProperties` | 默认 `true` |
| `KeepAlivePeriod` | `ExtendedProperties` | 默认 `60` 秒 |
| `AutoReconnect` | `ExtendedProperties` | 默认 `true` |
| `ReconnectDelay` | `ExtendedProperties` | 默认 `5000` 毫秒 |
| `WillTopic` / `WillMessage` | `ExtendedProperties` | 遗嘱消息 |
| `SubscribeTopics` / `Topics` | `ExtendedProperties` | 字符串数组或逗号分隔字符串 |

最小设备示例：

```csharp
var device = new DeviceInfo
{
    DeviceId = "mqtt-device-001",
    CommunicationType = CommunicationType.Mqtt,
    ConnectionMode = ConnectionMode.Client,
    ProtocolType = ProtocolType.Mqtt,
    IpAddress = "127.0.0.1",
    Port = 1883,
    ExtendedProperties =
    {
        ["ClientId"] = "mqtt-device-001",
        ["SubscribeTopics"] = "devices/001/telemetry,devices/001/status",
        ["Qos"] = "1",
        ["AutoReconnect"] = "true"
    }
};
```

## 强类型 MQTT API

`IMqttMessagingClient` 适合直接发布/订阅业务消息：

```csharp
var client = provider.GetRequiredService<IMqttMessagingClient>();

await client.ConnectAsync();
await client.SubscribeAsync(new MqttSubscribeOptions
{
    TopicFilter = "devices/+/telemetry",
    Qos = MqttQualityOfService.AtLeastOnce
});

await client.PublishAsync(new MqttPublishOptions
{
    Topic = "devices/001/commands",
    Payload = Encoding.UTF8.GetBytes("{\"action\":\"read\"}"),
    Qos = MqttQualityOfService.AtLeastOnce,
    Retain = false
});
```

## 生产注意事项

- `UseInMemoryTransport` 仅用于单元测试和本地开发，不要在生产环境启用。
- MQTT TLS、用户名、密码应来自安全配置源，不要硬编码到模板。
- HTTP 通道的非 2xx 响应会触发错误事件并返回发送失败。
- HTTP/MQTT 的真实网络可用性应纳入部署环境健康检查。
