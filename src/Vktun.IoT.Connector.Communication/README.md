# Vktun.IoT.Connector.Communication

通信通道实现，支持 TCP、UDP、MQTT 等多种通信协议。

## 安装

```bash
dotnet add package Vktun.IoT.Connector.Communication
```

## 功能

- TCP 客户端/服务器通道
- UDP 通道
- HTTP Client 通道
- MQTT 通道
- 安全 TCP 通道（SSL/TLS）

## HTTP/MQTT 文档

- [HTTP/MQTT 通道 NuGet 使用指南](../../Docs/HTTP-MQTT通道NuGet使用指南.md)
- `UseInMemoryTransport` 仅用于测试和本地开发，生产环境应连接真实 MQTT broker。
