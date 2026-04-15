# TODO

## 目标

把当前项目从"协议 SDK 骨架 + Demo 集合"推进到"可低成本二次开发的工业协议连接器平台"。

核心原则：

- 先打通真实采集链路，再扩协议数量。
- 先统一协议配置模型，再做可视化配置器。
- 先建立自动化回归闭环，再做大规模协议适配。

## 当前状态

截至本轮 P0+P1 落地，项目已经完成以下基础设施：

- 已打通 `DeviceManager -> TaskScheduler -> DeviceCommandExecutor -> Channel -> Parser -> DeviceData` 主链路。
- 已引入 `IDeviceCommandExecutor` 与 `ICommunicationChannelFactory` 运行时抽象。
- 已实现真实 TCP Client、TCP Server、UDP、Serial 通道基础收发能力。
- 已让 `TaskScheduler` 调用真实执行器，而不是 `Delay(100)` 桩逻辑。
- 已在 `ProtocolConfig` 上增加统一协议定义入口 `DefinitionJson`，并保留对旧 `ParseRules` 的兼容。
- 已支持 `protocolId` 获取 parser 的基础注册链路。
- 主库、Demo、DeviceMock 已可编译通过。
- 已建立设备断线、超时、重连、取消的统一状态机。
- 已支持调度器失败重试、重试退避、最大重试次数和真正的优先级调度。
- 已明确 `ProtocolConfig` 与具体协议配置的组合关系，增加强校验和错误报告。
- 已为解析器注册增加元数据（名称、版本、适用设备、支持能力、作者、状态）。
- 已支持按 `ProtocolType + ProtocolVersion + DeviceModel` 组合选择解析器。
- 已支持插件式协议装载。
- 已补齐 TCP 请求响应交互模型、UDP 会话管理和超时下线策略、串口帧间隔和多设备从站管理。
- 已新增 HTTP Client 通道，支持 `IHttpClientFactory` 与连接池配置，用于高并发 HTTP 设备/API 接入。
- 已新增 MQTT 专用易用 API，并将 `MqttChannel` 接入真实 MQTT broker 发布/订阅链路。
- 已统一通道层事件（连接、断连、收包、发包、异常、统计）。
- 已建立协议回归测试工程和基础回归用例。
- 已设计统一地址/点位抽象模型。
- 已增强自定义协议能力（变长帧、分隔符帧、位段解析、校验规则、反向打包）。
- 已做出协议调试工具（十六进制收发面板、错误定位、解析预览）。
- 已完善配置管理（模板导入导出、版本管理、热加载、校验报告）。

## P0

### 1. 打通真实采集执行链路

- [x] 设计并实现统一的数据流：`Device -> Session -> Channel -> Driver -> Request -> Response -> Parser -> DeviceData`
- [x] 在 `IoTDataCollector` 中接入真实采集流程，而不是只返回简单 `RawData`
- [x] 在 `DeviceManager` 的连接逻辑中真正创建并打开通信通道
- [x] 将 `CollectDataAsync` 改为真实采集流程
- [x] 将 `SendCommandAsync` 改为真实请求下发与响应等待流程
- [x] 建立设备断线、超时、重连、取消的统一状态机

### 2. 重构任务调度器为真实执行器

- [x] 为任务引入执行上下文：设备、协议、通道、超时、取消令牌
- [x] 支持请求响应关联，而不是单纯 `Delay(100)` 后返回成功
- [x] 支持失败重试、重试退避、最大重试次数
- [x] 支持真正的优先级调度，而不是仅保存优先级字段
- [x] 区分"任务队列长度"和"正在运行任务数"的真实指标
- [x] 为调度器补齐单元测试和并发测试

### 3. 统一协议配置模型

- [x] 为 `ProtocolConfig` 增加统一协议定义入口 `DefinitionJson`
- [x] 为配置模型增加版本号、厂商、设备型号、模板来源等字段
- [x] 让配置加载器把模板 JSON 映射到统一模型，同时兼容旧 `ParseRules`
- [x] 明确 `ProtocolConfig` 与具体协议配置的最终关系：继承、组合或判别联合
- [x] 逐步移除对 `ParseRules["CustomProtocolJson"]`、`ParseRules["ModbusConfig"]` 等隐式键的强依赖
- [x] 为配置加载增加强校验和错误报告

### 4. 修复协议解析器工厂与扩展机制

- [x] 修复 `protocolId` 注册与获取链路
- [x] 支持 `GetParser(string protocolId)`
- [x] 支持按 `ProtocolType + ProtocolVersion + DeviceModel` 组合选择解析器
- [x] 为解析器注册增加元数据：名称、版本、适用设备、支持能力、作者、状态
- [x] 支持插件式协议装载，减少新增协议时对主工程的侵入

### 5. 做实 TCP/UDP/串口通道

- [x] TCP Client 通道接入真实 Socket 驱动
- [x] TCP Server 通道修复为可编译、可收发的基础实现
- [x] UDP 通道修复为可编译、可收发的基础实现
- [x] 串口通道接入 `SerialPortDriver`，实现真实收发
- [x] TCP 通道补齐更完整的请求响应交互模型
- [x] UDP 通道补齐更稳定的设备识别、会话管理、超时下线策略
- [x] 串口链路补齐帧间隔、读写超时、轮询、共享串口、多设备从站管理
- [x] HTTP Client 通道接入 `IHttpClientFactory`，支持高并发请求、连接池和响应事件回传
- [x] MQTT 通道接入真实 MQTT broker，支持发布、订阅、QoS、retain、自动重连和订阅恢复
- [x] 为 MQTT 提供面向 NuGet 使用者的强类型发布/订阅客户端 API
- [x] 统一通道层事件：连接、断连、收包、发包、异常、统计

## P1

### 6. 建立协议回归测试闭环

- [x] 以 `Vktun.IoT.Connector.DeviceMock` 为基础建立自动化回归测试工程
- [x] 为 Modbus TCP、Modbus RTU、S7、IEC104 建立最小可运行回归用例
- [x] 补齐边界测试：空帧、短帧、CRC 错误、长度错误、异常码、字节序错误
- [x] 建立协议样例报文库，支持请求/响应回放
- [x] 建立模板兼容性测试，确保模板变更不会破坏旧协议

### 7. 设计统一地址/点位抽象

- [x] 定义跨协议统一的点位描述模型
- [x] 为 Modbus、S7、自定义协议建立统一地址表达
- [x] 支持地址解析与规范化输出
- [x] 支持位、字节、字、双字、浮点、字符串、BCD、枚举映射
- [ ] 支持同一设备多协议点位或多模板点位并存

### 8. 增强自定义协议能力

- [x] 补齐校验规则：CRC16/CRC32/LRC/XOR/SUM 的统一配置化实现
- [x] 支持变长帧、分隔符帧、带设备号帧、带序列号帧
- [x] 支持位段解析、枚举映射、条件字段、动态长度字段
- [x] 支持反向打包，用于指令下发
- [x] 支持原始值、转换值、质量位、错误原因同时输出

### 9. 做出真正可用的协议调试工具

- [x] 将 Demo 客户端的随机读写逻辑替换为真实协议收发
- [x] 提供十六进制收发面板、原始报文查看、解析结果预览
- [x] 支持从模板直接发起调试
- [x] 支持导入样例报文并回放
- [x] 支持错误定位：帧头不匹配、长度错误、校验失败、偏移越界

### 10. 完善配置管理

- [x] 支持模板导入导出
- [x] 支持模板版本管理和回滚
- [x] 支持热加载与生效范围控制
- [ ] 支持设备与协议模板的绑定关系管理
- [x] 支持配置合法性校验报告

### 11. HTTP/MQTT 通道产品化

- [x] 新增 `CommunicationType.Http` 与 `CommunicationType.Mqtt`，避免使用 TCP 语义承载 HTTP/MQTT 场景
- [x] 新增 `HttpProtocolParser`，支持 HTTP 原始响应透传与现有 `DeviceCommandExecutor` 链路兼容
- [x] 新增 `IMqttMessagingClient`、`MqttPublishOptions`、`MqttSubscribeOptions` 等强类型 MQTT API
- [x] 增加 HTTP 通道单元测试、MQTT topic filter 测试和本机 MQTT broker 集成测试
- [ ] 为 HTTP 设备配置补充 README 示例：`Url`、`BaseUrl`、`Path`、`Method`、`Headers`、`ContentType`
- [ ] 为 MQTT 设备配置补充 README 示例：Broker、ClientId、认证、TLS、QoS、retain、订阅主题
- [ ] 明确 `UseInMemoryTransport` 仅用于测试/本地开发，避免 NuGet 使用者误用于生产
- [x] 为 MQTT 增加异常场景回归：broker 不可达、认证失败、订阅失败、重连后恢复订阅
- [x] 为 HTTP 增加异常场景回归：非 2xx 状态码、超时、取消、响应体为空、并发压测

### 12. NuGet 包定位与发布治理

- [x] 明确 `Vktun.IoT.Connector` 作为推荐安装的主入口门面包
- [x] 明确 `Vktun.IoT.Connector.Core` 作为接口、模型、枚举和公共契约包
- [x] 明确 `Vktun.IoT.Connector.Protocol` 作为协议解析和命令打包能力包
- [x] 明确 `Vktun.IoT.Connector.Communication` 作为 TCP/UDP/HTTP/MQTT 通道实现包
- [x] 明确 `Vktun.IoT.Connector.Serial` 作为可选串口通信扩展包
- [x] 修正主包对运行时实现项目的 NuGet 依赖透传，避免消费者安装主包后缺少实现程序集
- [x] 为已有子包 README 配置 `PackageReadmeFile`，提升 NuGet 包页可读性
- [x] 将 `Vktun.IoT.Connector.Business` 长期规划为 `Runtime` 或 `Hosting` 定位，避免以 Business 命名作为开发者主入口
- [x] 评估将 Azure/AWS 云连接器从运行时编排层拆分为独立云平台包
- [x] 评估将 `Communication` 按传输方式拆分为 TCP/UDP、HTTP、MQTT 等可选包，减少无关依赖
- [x] 为主包增加 DI 注册入口示例，例如 `AddVktunIoTConnector`、`AddVktunHttpChannel`、`AddVktunMqttChannel`
- [x] 新增 NuGet 包规划与拆分评估文档：`Docs/NuGet包规划与拆分评估.md`

## P2

### 13. 完善运行态能力

- [x] 资源监控接入真实连接数、线程数、Socket 数、吞吐量
- [x] 增加设备维度和协议维度的指标统计
- [x] 增加慢请求、超时率、异常率、重连率统计
- [x] 为日志增加结构化字段：设备 ID、通道 ID、协议 ID、任务 ID
- [x] 增加问题定位链路：请求帧、响应帧、解析错误、配置版本

### 14. 完善缓存与持久化策略

- [x] 明确缓存用途：最近值、历史值、回放值
- [x] 区分内存缓存与持久化存储
- [x] 增加背压策略与写入失败降级策略
- [x] 支持可选存储后端：内存、文件、SQLite、外部数据库

### 15. 文档与工程一致性治理

- [x] 修复 `TODO.md` 与短期路线图文档编码
- [x] 统一 README、README.zh、模板文件、Demo 文档的编码
- [x] 删除或标记仍为桩实现的功能，避免误导使用者
- [x] 为新增协议编写标准接入文档
- [x] 为 HTTP/MQTT 通道编写 NuGet 使用指南和最小可运行示例
- [x] 为 NuGet 主包和子包补齐安装矩阵、依赖关系图和推荐安装路径
- [x] 为模板编写字段说明文档和示例文档
- [x] 建立"从新增设备到完成接入"的标准流程说明

## 当前验证结论

- [x] `src/Vktun.IoT.Connector` 构建通过
- [x] `demo/Vktun.IoT.Connector.Demo` 构建通过
- [x] `demo/Vktun.IoT.Connector.DeviceMock` 构建通过
- [x] `tests/Vktun.IoT.Connector.UnitTests` 构建通过
- [x] HTTP 通道单元测试通过
- [x] MQTT 本机 broker 发布/订阅集成测试通过
- [x] HTTP/MQTT 异常场景回归测试通过
- [x] `tests/Vktun.IoT.Connector.UnitTests` 通过：68 passed
- [x] `tests/Vktun.IoT.Connector.ProtocolTests` 构建通过
- [ ] 真实设备联调验证
- [ ] 自动化回归验证

## 环境备注

当前环境下构建建议统一使用：

- `DOTNET_CLI_HOME=.dotnet-home`
- `MSBuildEnableWorkloadResolver=false`
- `dotnet build --no-restore -m:1`

原因：

- 当前 .NET 10 / MSBuild 环境存在 workload resolver 假失败。
- 并行构建容易触发 `obj` 文件锁，串行构建更稳定。
