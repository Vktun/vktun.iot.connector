# Vktun.IoT.Connector.Business

业务运行时编排层，提供设备管理、会话管理、命令执行、数据提供和重试等核心能力。云连接器类存在于该层，但不等同于主门面默认公开的稳定能力。

## 安装

```bash
dotnet add package Vktun.IoT.Connector.Business
```

## 功能

- 设备管理器
- 会话管理器
- 心跳管理器
- 云连接器辅助类（Azure IoT Hub、AWS IoT Core）
- 数据提供者
- 认证服务
- 命令执行器
- 重试策略

## 当前能力边界

- `AzureIoTHubConnector` 和 `AwsIoTConnector` 当前以独立辅助类形态存在，未纳入 `AddVktunIoTConnector` 的默认注册链路。
- 它们现在支持通过 `AddVktunAzureIoTHubConnector`、`AddVktunAwsIoTConnector` 显式注册，适合按需集成而不是默认启用。
- 运行时主路径仍以设备管理、通道工厂、命令执行、会话与数据提供为主。
