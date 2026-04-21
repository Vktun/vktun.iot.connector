# Vktun.IoT.Connector

物联网设备连接 SDK 的主门面包。当前稳定入口聚焦 `IIoTDataCollector`、DI 注册、Modbus RTU/TCP、HTTP/MQTT 和自定义协议接入；S7/IEC104 为受限可用，实验性协议和云连接器不应直接视为主包的生产承诺。

## 安装

```bash
dotnet add package Vktun.IoT.Connector
```

## 功能

- `IIoTDataCollector` 主门面
- `AddVktunIoTConnector` / `AddVktunHttpChannel` / `AddVktunMqttChannel`
- 设备管理与数据采集
- Modbus RTU/TCP、HTTP/MQTT、自定义协议的对外接入路径
- 受限的 S7 / IEC104 运行时能力

## 快速开始

```csharp
using Microsoft.Extensions.DependencyInjection;
using Vktun.IoT.Connector;
using Vktun.IoT.Connector.Core.Interfaces;

var services = new ServiceCollection();

services.AddVktunIoTConnector(options =>
{
    options.ConfigureSdk = config =>
    {
        config.Global.MaxConcurrentConnections = 1000;
        config.Http.MaxConnectionsPerServer = 512;
    };
});

await using var provider = services.BuildServiceProvider();
var collector = provider.GetRequiredService<IIoTDataCollector>();

await collector.InitializeAsync();
await collector.StartAsync();
```

## 按通道注册

仅使用 HTTP 通道：

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddVktunHttpChannel(options =>
{
    options.ConfigureSdk = config =>
    {
        config.Http.RequestTimeout = 5000;
        config.Http.MaxConnectionsPerServer = 512;
    };
});
```

仅使用 MQTT 通道：

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddVktunMqttChannel(mqtt =>
{
    mqtt.Server = "127.0.0.1";
    mqtt.Port = 1883;
    mqtt.ClientId = "gateway-001";
});
```

## 当前能力边界

- `OPC UA`、`BACnet`、`CANopen` 在当前代码库里属于实验性能力，请以根 README 和 `Docs/实现状态与桩功能说明.md` 为准。
- `AzureIoTHubConnector`、`AwsIoTConnector` 存在于运行时层，但不是 `AddVktunIoTConnector` 默认注册的一部分。
- 如需按需接入云连接器，请显式调用 `AddVktunAzureIoTHubConnector` 或 `AddVktunAwsIoTConnector`。
- 生产场景请不要启用 `UseInMemoryTransport`，并应补充真实设备联调与证书策略验证。

## 延伸文档

- [HTTP/MQTT 通道 NuGet 使用指南](../../Docs/HTTP-MQTT通道NuGet使用指南.md)
- [NuGet 安装矩阵与依赖关系](../../Docs/NuGet安装矩阵与依赖关系.md)
- [新增设备接入流程](../../Docs/新增设备接入流程.md)
