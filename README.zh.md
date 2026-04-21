# Vktun.IoT.Connector

工业设备数据采集 SDK，提供 DI 门面、通信通道、协议解析、模板和示例工具。当前已验证路径聚焦 Modbus RTU/TCP、HTTP/MQTT 和自定义协议场景；其他协议能力请以文档中的成熟度说明为准。

**语言**: [English](README.md) | [中文](README.zh.md)

## 当前能力快照

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| 主门面 + DI 入口 | 已验证完整 | `IIoTDataCollector`、`AddVktunIoTConnector`、`AddVktunHttpChannel`、`AddVktunMqttChannel` 有构建和测试覆盖。 |
| TCP / UDP / Serial 通道 | 受限可用 | 已接入真实收发链路，但真实设备联调仍然必需。 |
| HTTP / MQTT 通道 | 已验证完整 | 有最小文档、DI 注册和测试；生产可用性仍依赖部署环境健康检查。 |
| Modbus RTU / TCP | 已验证完整 | 解析、打包、模板兼容和回归测试都已具备。 |
| 自定义协议 | 受限可用 | 已支持 JSON 驱动解析，但点位正确性仍依赖真实样例报文验证。 |
| S7 | 受限可用 | 需要走专用读写命令入口，不应直接使用通用 `BuildRequest` 桩入口。 |
| IEC104 | 受限可用 | 已有解析入口，但完整 ASDU 指令建模仍受限。 |
| OPC UA / BACnet / CANopen | 实验性 | 代码存在，但当前通过 `ProtocolType.Custom` 路径归类，不应视为生产能力。 |
| Azure / AWS 云连接器 | 显式接入 | 现在可通过专门 DI 扩展方法按需注册，但它们仍不属于 `AddVktunIoTConnector` 的默认运行时链路。 |
| DeviceMock | 仅开发使用 | 适合开发和回归，不是生产网关服务。 |

## 项目结构

```
Vktun.IoT.Connector/
├── src/                                    # 源代码目录
│   ├── Vktun.IoT.Connector/               # 应用接口层 - SDK入口API
│   │
│   ├── Vktun.IoT.Connector.Business/      # 业务逻辑层 - 核心业务处理
│   │
│   ├── Vktun.IoT.Connector.Concurrency/   # 并发调度层 - 任务调度与资源监控
│   │
│   ├── Vktun.IoT.Connector.Communication/ # 通信适配层 - 通信通道实现
│   │
│   ├── Vktun.IoT.Connector.Serial/        # 串口通信模块 - 独立串口采集包
│   │
│   ├── Vktun.IoT.Connector.Driver/        # 底层驱动层 - 硬件驱动封装
│   │
│   ├── Vktun.IoT.Connector.Protocol/      # 协议解析层 - 协议解析器
│   │
│   ├── Vktun.IoT.Connector.Configuration/ # 配置管理层 - 配置与日志
│   │
│   └── Vktun.IoT.Connector.Core/          # 核心层 - 接口与模型定义
│
└── demo/                                  # 示例程序
    └── Vktun.IoT.Connector.Demo/
        └── Program.cs                     # 上位机采集演示程序
```

## NuGet 快速接入

推荐外部项目优先安装主包：

```bash
dotnet add package Vktun.IoT.Connector
```

使用默认运行时注册：

```csharp
using Microsoft.Extensions.DependencyInjection;
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

只注册 HTTP 或 MQTT 通道：

```csharp
services.AddVktunHttpChannel();

services.AddVktunMqttChannel(mqtt =>
{
    mqtt.Server = "127.0.0.1";
    mqtt.Port = 1883;
    mqtt.ClientId = "gateway-001";
});
```

## 文档索引

| 文档 | 用途 |
| --- | --- |
| [实现状态与桩功能说明](Docs/实现状态与桩功能说明.md) | 标记仍受限或仅用于测试/演示的能力 |
| [新增协议标准接入指南](Docs/新增协议标准接入指南.md) | 新增协议的代码、模板、测试和文档流程 |
| [HTTP/MQTT 通道 NuGet 使用指南](Docs/HTTP-MQTT通道NuGet使用指南.md) | HTTP/MQTT 注册、设备字段和最小示例 |
| [NuGet 安装矩阵与依赖关系](Docs/NuGet安装矩阵与依赖关系.md) | 主包和子包的推荐安装路径 |
| [协议模板字段说明](Docs/协议模板字段说明.md) | 模板公共字段、点位字段和协议字段说明 |
| [新增设备接入流程](Docs/新增设备接入流程.md) | 从设备信息收集到现场联调的完整流程 |

## 生产使用注意事项

- 当前目标框架是 `net10.0`，本仓库构建/测试基线依赖预览 SDK。
- 对外宣称协议范围前，先阅读 [实现状态与桩功能说明](Docs/实现状态与桩功能说明.md)。
- `UseInMemoryTransport` 仅用于测试和本地开发。
- `DeviceMock` 是开发/回归工具，不是生产网关服务。
- `OPC UA`、`BACnet`、`CANopen` 在当前代码库中属于实验性能力。
- `AzureIoTHubConnector`、`AwsIoTConnector` 需要显式调用 `AddVktunAzureIoTHubConnector`、`AddVktunAwsIoTConnector` 才会注册。

## 架构设计

### 五层架构

```
┌─────────────────────────────────────────────────────────────┐
│                    应用接口层 (Api)                          │
│              IIoTDataCollector / IoTDataCollector           │
├─────────────────────────────────────────────────────────────┤
│                    业务逻辑层 (Business)                     │
│     DeviceManager / SessionManager / HeartbeatManager       │
├─────────────────────────────────────────────────────────────┤
│                   并发调度层 (Concurrency)                   │
│          TaskScheduler / ResourceMonitor / AsyncQueue       │
├─────────────────────────────────────────────────────────────┤
│                  通信适配层 (Communication)                  │
│         TcpServerChannel / UdpChannel / SerialChannel       │
├─────────────────────────────────────────────────────────────┤
│                    底层驱动层 (Driver)                       │
│              SocketDriver / SerialPortDriver                │
└─────────────────────────────────────────────────────────────┘
```

## 协议解析

### 支持的协议

| 协议类型 | 解析器 | 说明 |
|---------|--------|------|
| Modbus RTU | `ModbusRtuParser` | 串口Modbus协议，CRC16校验 |
| Modbus TCP | `ModbusTcpParser` | TCP Modbus协议，MBAP头 |
| 自定义协议 | `CustomProtocolParser` | JSON配置的灵活协议解析 |

### Modbus 协议解析

#### 寄存器类型

| 类型 | 枚举值 | 功能码 | 访问权限 |
|------|--------|--------|----------|
| 线圈 (Coil) | `Coil` | 01/05/0F | 读写 |
| 离散输入 (Discrete Input) | `DiscreteInput` | 02 | 只读 |
| 输入寄存器 (Input Register) | `InputRegister` | 04 | 只读 |
| 保持寄存器 (Holding Register) | `HoldingRegister` | 03/06/10 | 读写 |

#### JSON 配置示例

```json
{
  "ProtocolId": "ModbusRtu_001",
  "ProtocolName": "温湿度采集",
  "ModbusType": "Rtu",
  "SlaveId": 1,
  "ByteOrder": "BigEndian",
  "WordOrder": "HighWordFirst",
  "Points": [
    {
      "PointName": "温度",
      "RegisterType": "InputRegister",
      "Address": 0,
      "Quantity": 1,
      "DataType": "Int16",
      "Ratio": 0.1,
      "Unit": "℃"
    },
    {
      "PointName": "湿度",
      "RegisterType": "InputRegister",
      "Address": 1,
      "DataType": "UInt16",
      "Ratio": 0.1,
      "Unit": "%RH"
    },
    {
      "PointName": "运行状态",
      "RegisterType": "Coil",
      "Address": 0,
      "DataType": "UInt8"
    }
  ]
}
```

#### 数据类型支持

| 数据类型 | 字节数 | 说明 |
|---------|--------|------|
| UInt8 / Int8 | 1 | 8位整数 |
| UInt16 / Int16 | 2 | 16位整数 |
| UInt32 / Int32 | 4 | 32位整数 |
| UInt64 / Int64 | 8 | 64位整数 |
| Float | 4 | 单精度浮点 |
| Double | 8 | 双精度浮点 |

### 自定义协议解析

通过JSON配置实现灵活的二进制协议解析：

```json
{
  "ProtocolId": "CustomProtocol_001",
  "FrameType": "VariableLength",
  "ByteOrder": "BigEndian",
  "FrameHeader": {
    "Value": [170, 85],
    "Length": 2
  },
  "FrameLength": {
    "Offset": 2,
    "Length": 2,
    "CalcRule": "Self"
  },
  "Points": [
    {
      "PointName": "温度",
      "Offset": 4,
      "Length": 2,
      "DataType": "UInt16",
      "Ratio": 0.1,
      "Unit": "℃"
    }
  ]
}
```

## CRC 校验工具

`CrcCalculator` 提供多种校验算法：

| 算法 | 方法 | 用途 |
|------|------|------|
| CRC16 Modbus | `Crc16Modbus()` | Modbus RTU |
| CRC16 CCITT | `Crc16Ccitt()` | 通信协议 |
| CRC32 | `Crc32()` | 以太网/ZIP |
| CRC8 | `Crc8()` | 8位校验 |
| LRC | `Lrc()` | Modbus ASCII |
| XOR | `XorCheck()` | 异或校验 |
| Sum | `SumCheck()` | 累加和 |

### 使用示例

```csharp
using Vktun.IoT.Connector.Core.Utils;

// CRC16 Modbus
ushort crc = CrcCalculator.Crc16Modbus(data);
byte crcLow = (byte)(crc & 0xFF);
byte crcHigh = (byte)(crc >> 8);

// 验证
bool valid = CrcCalculator.VerifyCrc16Modbus(receivedData, expectedCrc);

// LRC
byte lrc = CrcCalculator.Lrc(data);
```

## 快速开始

### 安装

```bash
dotnet add package Vktun.IoT.Connector
```

### 基本使用

```csharp
using Microsoft.Extensions.DependencyInjection;
using Vktun.IoT.Connector;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

var services = new ServiceCollection();
services.AddVktunIoTConnector();

await using var provider = services.BuildServiceProvider();
var collector = provider.GetRequiredService<IIoTDataCollector>();

await collector.InitializeAsync();
await collector.StartAsync();

// 配置设备
var device = new DeviceInfo
{
    DeviceId = "DEVICE_001",
    DeviceName = "温湿度传感器",
    CommunicationType = CommunicationType.Tcp,
    ConnectionMode = ConnectionMode.Client,
    IpAddress = "192.168.1.10",
    Port = 502,
    ProtocolType = ProtocolType.ModbusTcp,
    ProtocolId = "modbus-template-001",
    ProtocolVersion = "1.0.0"
};

await collector.AddDeviceAsync(device);
await collector.ConnectDeviceAsync(device.DeviceId);

// 采集数据
var data = await collector.CollectDataAsync(device.DeviceId);
```

## 客户端工具

SDK 包含一个面向协议调试和演示场景的 WPF 客户端工作台。

### 当前工作台范围

- **已验证路径**：Modbus RTU、Modbus TCP、西门子 S7、串口调试。
- **演示级页面**：客户端中包含三菱和欧姆龙页面，但当前没有 DeviceMock 回归闭环，也不应直接视为生产已验证能力。

### 客户端截图

#### Modbus RTU 客户端

![Modbus RTU 客户端](docs/modubsrtu.png)

#### 西门子 S7 客户端

![西门子 S7 客户端](docs/s7.png)

### 功能特性

- 实时数据监控与可视化
- 协议配置与测试
- 连接状态监控
- 数据记录与分析
- 单一界面支持多协议

## 项目依赖关系

```
Vktun.IoT.Connector
    ├── Vktun.IoT.Connector.Business
    │   ├── Vktun.IoT.Connector.Core
    │   ├── Vktun.IoT.Connector.Concurrency
    │   │   └── Vktun.IoT.Connector.Core
    │   ├── Vktun.IoT.Connector.Communication
    │   │   ├── Vktun.IoT.Connector.Core
    │   │   └── Vktun.IoT.Connector.Driver
    │   │       └── Vktun.IoT.Connector.Core
    │   ├── Vktun.IoT.Connector.Protocol
    │   │   └── Vktun.IoT.Connector.Core
    │   └── Vktun.IoT.Connector.Configuration
    │       └── Vktun.IoT.Connector.Core
    └── Vktun.IoT.Connector.Configuration
        └── Vktun.IoT.Connector.Core

Vktun.IoT.Connector.Serial (独立包)
    └── Vktun.IoT.Connector.Core
```

## 技术特性

- **.NET 10.0**: 当前目标框架，仓库基线使用预览 SDK 验证
- **异步编程**: async/await + IOCP 模型
- **依赖注入**: 接口解耦，易于测试
- **插件化设计**: 协议解析器可扩展
- **高并发**: 连接池、线程池、任务队列
- **跨平台**: Windows/Linux/macOS

## 许可证

MIT License
