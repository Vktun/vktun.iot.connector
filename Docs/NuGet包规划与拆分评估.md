# NuGet 包规划与拆分评估

## 目标

让外部开发者安装 NuGet 包时能快速判断应该安装哪个包，并避免因为一个场景引入过多无关依赖。

## 推荐安装路径

| 使用场景 | 推荐包 | 说明 |
| --- | --- | --- |
| 完整 SDK 接入 | `Vktun.IoT.Connector` | 主入口门面包，包含设备管理、运行时编排、协议解析、通信通道和默认配置能力 |
| 自定义协议或插件开发 | `Vktun.IoT.Connector.Core` | 只依赖公共接口、模型、枚举和基础契约 |
| 只做协议解析/命令打包 | `Vktun.IoT.Connector.Protocol` | 不直接依赖具体通信通道 |
| 只使用通信通道 | `Vktun.IoT.Connector.Communication` | 当前包含 TCP、UDP、HTTP、MQTT、Secure TCP |
| 串口设备接入 | `Vktun.IoT.Connector.Serial` | 串口能力单独作为可选包 |
| 使用默认 JSON 配置和日志 | `Vktun.IoT.Connector.Configuration` | 配置文件、日志、证书等默认实现 |

## `Business` 长期定位

`Vktun.IoT.Connector.Business` 当前承担的是运行时编排能力，而不是普通业务逻辑。它包含：

- `DeviceManager`
- `SessionManager`
- `HeartbeatManager`
- `DeviceCommandExecutor`
- `CommunicationChannelFactory`
- 云连接器
- 数据提供者
- 重试策略

长期建议将该项目定位为运行时层，并在后续版本中迁移命名：

```text
Vktun.IoT.Connector.Business -> Vktun.IoT.Connector.Runtime
```

如果未来需要面向 ASP.NET Core Worker、Windows Service、边缘网关等宿主形态，可以再增加：

```text
Vktun.IoT.Connector.Hosting
```

`Runtime` 负责核心运行编排，`Hosting` 负责宿主集成、后台服务、健康检查和配置绑定。

## 云连接器拆包评估

当前 Azure/AWS 云连接器在 `Business` 中，会导致安装运行时编排层时同时拉入云平台依赖。长期建议拆分：

```text
Vktun.IoT.Connector.Cloud.Azure
Vktun.IoT.Connector.Cloud.Aws
```

拆分收益：

- 本地采集场景不再引入云 SDK。
- Azure/AWS 能独立发布和修复。
- 云平台认证、重试、设备影子、直连方法等能力可以按平台演进。

短期策略：

- `0.0.x` 阶段先保留在 `Business`，降低迁移成本。
- 对云连接器文档标注为可选能力。
- `0.1.x` 或更高版本再拆独立包。

## `Communication` 拆包评估

当前 `Vktun.IoT.Connector.Communication` 同时包含 TCP、UDP、HTTP、MQTT。由于 HTTP 依赖 `Microsoft.Extensions.Http`，MQTT 依赖 `MQTTnet`，只使用 TCP/UDP 的用户也会获得这些依赖。

长期建议拆分为：

```text
Vktun.IoT.Connector.Transport.TcpUdp
Vktun.IoT.Connector.Transport.Http
Vktun.IoT.Connector.Transport.Mqtt
Vktun.IoT.Connector.Transport.SecureTcp
```

短期策略：

- `0.0.x` 阶段继续保留统一 `Communication` 包。
- README 明确 HTTP/MQTT 是可选使用场景。
- 稳定后再按传输方式拆包，主包继续聚合这些传输包。

## 主包 DI 入口

主包提供以下 DI 扩展入口：

```csharp
services.AddVktunIoTConnector();
services.AddVktunHttpChannel();
services.AddVktunMqttChannel();
```

推荐普通使用者优先安装并使用：

```csharp
services.AddVktunIoTConnector(options =>
{
    options.ConfigureSdk = config =>
    {
        config.Http.MaxConnectionsPerServer = 512;
        config.Global.MaxConcurrentConnections = 1000;
    };
});
```

只使用 HTTP 或 MQTT 通道时，可以使用更轻的注册方式：

```csharp
services.AddVktunHttpChannel();

services.AddVktunMqttChannel(mqtt =>
{
    mqtt.Server = "127.0.0.1";
    mqtt.Port = 1883;
    mqtt.ClientId = "gateway-001";
});
```

## 阶段计划

### `0.0.x`

- 保持现有包名。
- 主推 `Vktun.IoT.Connector`。
- 补齐 DI、README、示例和测试。
- 避免 demo 项目被打包发布。

### `0.1.x`

- 新增 `Runtime` 或 `Hosting` 定位。
- 将云连接器从运行时层拆出为独立包。
- 评估传输层按 TCP/UDP、HTTP、MQTT 拆包。

### `1.0`

- 固化公共契约包。
- 固化主包和可选扩展包依赖关系。
- 对包名、命名空间、DI 入口做兼容性承诺。
