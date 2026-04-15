# NuGet 安装矩阵与依赖关系

本文档说明各包定位、推荐安装路径和依赖关系，避免使用者安装子包后缺少运行时实现。

## 推荐路径

| 使用场景 | 推荐安装 | 说明 |
| --- | --- | --- |
| 应用接入 SDK、采集设备、使用 DI 注册 | `Vktun.IoT.Connector` | 推荐默认选择，包含门面 API 与运行时依赖 |
| 只引用接口、模型、枚举 | `Vktun.IoT.Connector.Core` | 用于插件、业务层契约或测试替身 |
| 只开发协议解析器 | `Vktun.IoT.Connector.Protocol` | 包含解析器与模板，不提供完整采集运行时 |
| 只开发通信通道 | `Vktun.IoT.Connector.Communication` | 包含 TCP/UDP/HTTP/MQTT 通道 |
| 只使用串口能力 | `Vktun.IoT.Connector.Serial` | 串口通道独立能力包 |
| 配置、日志、证书管理扩展 | `Vktun.IoT.Connector.Configuration` | 配置加载和日志实现 |
| 任务调度与资源监控扩展 | `Vktun.IoT.Connector.Concurrency` | 调度队列、资源监控 |

## 包依赖关系

```text
Vktun.IoT.Connector
├── Vktun.IoT.Connector.Core
├── Vktun.IoT.Connector.Business
├── Vktun.IoT.Connector.Communication
├── Vktun.IoT.Connector.Serial
├── Vktun.IoT.Connector.Protocol
├── Vktun.IoT.Connector.Configuration
└── Vktun.IoT.Connector.Concurrency
```

运行时核心依赖：

```text
Business
├── Core
├── Communication
├── Serial
├── Protocol
├── Configuration
└── Concurrency
```

## 子包边界

| 包 | 应包含 | 不应包含 |
| --- | --- | --- |
| `Core` | 接口、模型、枚举、公共工具 | 具体通道、云 SDK、数据库驱动 |
| `Protocol` | 解析器、模板、协议命令打包 | 设备连接生命周期、通道实现 |
| `Communication` | TCP/UDP/HTTP/MQTT 通道 | 协议解析规则、设备管理 |
| `Serial` | 串口驱动与串口通道 | TCP/UDP/MQTT 依赖 |
| `Business` | 设备管理、会话、命令执行、数据提供 | 用户入口文档的主要宣传名称 |
| `Vktun.IoT.Connector` | DI 扩展、门面 API、推荐入口 | 与子包重复的大量实现代码 |

## 安装建议

普通应用：

```bash
dotnet add package Vktun.IoT.Connector
```

协议插件：

```bash
dotnet add package Vktun.IoT.Connector.Core
dotnet add package Vktun.IoT.Connector.Protocol
```

通信通道扩展：

```bash
dotnet add package Vktun.IoT.Connector.Core
dotnet add package Vktun.IoT.Connector.Communication
```

## 发布检查

- 每个可发布项目必须配置 `PackageReadmeFile`。
- 主包 README 必须把 `Vktun.IoT.Connector` 标为推荐安装入口。
- 子包 README 必须说明“不包含完整运行时”或其能力边界。
- 新增第三方依赖时，必须确认是否应放在主运行时包、可选扩展包或测试项目中。
