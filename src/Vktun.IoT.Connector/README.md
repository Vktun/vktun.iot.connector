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
using Vktun.IoT.Connector;

var collector = new IoTDataCollector();
await collector.StartAsync();
```
