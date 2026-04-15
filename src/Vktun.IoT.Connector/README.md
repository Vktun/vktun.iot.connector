# Vktun.IoT.Connector

物联网设备连接 SDK，支持 Modbus RTU/TCP、IEC104、S7、OPC UA 等多种工业协议，提供设备管理、数据采集、云端连接等完整功能。

## 安装

```bash
dotnet add package Vktun.IoT.Connector
```

## 功能

- 多协议支持（Modbus、IEC104、S7、OPC UA 等）
- 设备管理
- 数据采集
- 云端连接
- 完整的 IoT 解决方案

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

## 延伸文档

- [HTTP/MQTT 通道 NuGet 使用指南](../../Docs/HTTP-MQTT通道NuGet使用指南.md)
- [NuGet 安装矩阵与依赖关系](../../Docs/NuGet安装矩阵与依赖关系.md)
- [新增设备接入流程](../../Docs/新增设备接入流程.md)
