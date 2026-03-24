# Vktun.IoT.Connector

工业设备数据采集 SDK，支持多种通信方式和协议解析，具备高并发、高可用、可扩展、跨平台的特性。

**语言**: [English](README.md) | [中文](README.zh.md)

## 项目结构

```
Vktun.IoT.Connector/
├── src/                                    # 源代码目录
│   ├── Vktun.IoT.Connector/               # 应用接口层 - SDK入口API
│   │   ├── IIoTDataCollector.cs           # 数据采集器接口
│   │   └── IoTDataCollector.cs            # 数据采集器实现
│   │
│   ├── Vktun.IoT.Connector.Business/      # 业务逻辑层 - 核心业务处理
│   │   ├── Managers/
│   │   │   ├── DeviceManager.cs           # 设备管理器
│   │   │   ├── SessionManager.cs          # 会话管理器
│   │   │   └── HeartbeatManager.cs        # 心跳管理器
│   │   └── Providers/
│   │       └── DataProvider.cs            # 数据提供者
│   │
│   ├── Vktun.IoT.Connector.Concurrency/   # 并发调度层 - 任务调度与资源监控
│   │   ├── Schedulers/
│   │   │   └── TaskScheduler.cs           # 任务调度器
│   │   ├── Queues/
│   │   │   └── AsyncQueue.cs              # 异步队列
│   │   └── Monitors/
│   │       └── ResourceMonitor.cs         # 资源监控器
│   │
│   ├── Vktun.IoT.Connector.Communication/ # 通信适配层 - 通信通道实现
│   │   └── Channels/
│   │       ├── CommunicationChannelBase.cs# 通信通道基类
│   │       ├── TcpServerChannel.cs        # TCP服务端通道
│   │       └── UdpChannel.cs              # UDP通道
│   │
│   ├── Vktun.IoT.Connector.Serial/        # 串口通信模块 - 独立串口采集包
│   │   ├── Channels/
│   │   │   ├── SerialChannelBase.cs       # 串口通道基类
│   │   │   └── SerialChannel.cs           # 串口通道实现
│   │   └── Drivers/
│   │       └── SerialPortDriver.cs        # 串口驱动(Windows API封装)
│   │
│   ├── Vktun.IoT.Connector.Driver/        # 底层驱动层 - 硬件驱动封装
│   │   └── Sockets/
│   │       └── SocketDriver.cs            # Socket驱动
│   │
│   ├── Vktun.IoT.Connector.Protocol/      # 协议解析层 - 协议解析器
│   │   ├── Parsers/
│   │   │   ├── CustomProtocolParser.cs    # 自定义协议解析器
│   │   │   ├── ModbusRtuParser.cs         # Modbus RTU解析器
│   │   │   └── ModbusTcpParser.cs         # Modbus TCP解析器
│   │   ├── Factories/
│   │   │   └── ProtocolParserFactory.cs   # 协议解析器工厂
│   │   └── Templates/                     # JSON配置模板
│   │       ├── ModbusRtuTemplate.json     # Modbus RTU配置模板
│   │       └── ModbusTcpTemplate.json     # Modbus TCP配置模板
│   │
│   ├── Vktun.IoT.Connector.Configuration/ # 配置管理层 - 配置与日志
│   │   ├── Providers/
│   │   │   └── JsonConfigurationProvider.cs# JSON配置提供者
│   │   └── Logging/
│   │       └── Logger.cs                  # 日志接口
│   │
│   └── Vktun.IoT.Connector.Core/          # 核心层 - 接口与模型定义
│       ├── Interfaces/                    # 核心接口
│       │   ├── IDeviceManager.cs          # 设备管理接口
│       │   ├── ISessionManager.cs         # 会话管理接口
│       │   ├── ICommunicationChannel.cs   # 通信通道接口
│       │   ├── IProtocolParser.cs         # 协议解析接口
│       │   ├── IDataProvider.cs           # 数据提供接口
│       │   └── ITaskScheduler.cs          # 任务调度接口
│       ├── Models/                        # 数据模型
│       │   ├── ConfigModels.cs            # 配置模型
│       │   ├── DeviceModels.cs            # 设备模型
│       │   ├── ProtocolModels.cs          # 协议模型
│       │   └── ModbusModels.cs            # Modbus模型
│       ├── Enums/
│       │   └── Enums.cs                   # 枚举定义
│       └── Utils/
│           └── CrcCalculator.cs           # CRC校验工具
│
└── demo/                                  # 示例程序
    └── Vktun.IoT.Connector.Demo/
        └── Program.cs                     # 上位机采集演示程序
```

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
using Vktun.IoT.Connector;
using Vktun.IoT.Connector.Business.Managers;
using Vktun.IoT.Connector.Configuration.Providers;
using Vktun.IoT.Connector.Core.Models;

// 初始化
var logger = new ConsoleLogger();
var configProvider = new JsonConfigurationProvider(logger);
var deviceManager = new DeviceManager(sessionManager, configProvider, logger);

var collector = new IoTDataCollector(
    deviceManager,
    sessionManager,
    taskScheduler,
    resourceMonitor,
    configProvider,
    dataProvider,
    heartbeatManager,
    logger);

// 配置设备
var device = new DeviceInfo
{
    DeviceId = "DEVICE_001",
    DeviceName = "温湿度传感器",
    CommunicationType = CommunicationType.Serial,
    SerialPort = "COM3",
    BaudRate = 9600,
    ProtocolType = ProtocolType.ModbusRtu
};

await collector.AddDeviceAsync(device);
await collector.ConnectDeviceAsync(device.DeviceId);

// 采集数据
var data = await collector.CollectDataAsync(device.DeviceId);
```

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

- **.NET 10.0**: 最新框架支持
- **异步编程**: async/await + IOCP 模型
- **依赖注入**: 接口解耦，易于测试
- **插件化设计**: 协议解析器可扩展
- **高并发**: 连接池、线程池、任务队列
- **跨平台**: Windows/Linux/macOS

## 许可证

MIT License
