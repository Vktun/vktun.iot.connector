# Vktun.IoT.Connector

Industrial device data acquisition SDK, supporting multiple communication methods and protocol parsing, with high concurrency, high availability, scalability, and cross-platform features.

**Language**: [English](README.md) | [中文](README.zh.md)

## Project Structure

```
Vktun.IoT.Connector/
├── src/                                    # Source Code Directory
│   ├── Vktun.IoT.Connector/               # Application Interface Layer - SDK Entry API
│   │
│   ├── Vktun.IoT.Connector.Business/      # Business Logic Layer - Core Business Processing
│   │
│   ├── Vktun.IoT.Connector.Concurrency/   # Concurrency Scheduling Layer
│   │
│   ├── Vktun.IoT.Connector.Communication/ # Communication Adapter Layer
│   │
│   ├── Vktun.IoT.Connector.Serial/        # Serial Communication Module - Independent Package
│   │
│   ├── Vktun.IoT.Connector.Driver/        # Hardware Driver Layer
│   │
│   ├── Vktun.IoT.Connector.Protocol/      # Protocol Parsing Layer
│   │
│   ├── Vktun.IoT.Connector.Configuration/ # Configuration Management Layer
│   │
│   └── Vktun.IoT.Connector.Core/          # Core Layer - Interfaces & Models
│
└── demo/                                  # Demo Application
    └── Vktun.IoT.Connector.Demo/
        └── Program.cs                     # Data Acquisition Demo
```

## Architecture Design

### Five-Layer Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                Application Interface Layer (Api)            │
│              IIoTDataCollector / IoTDataCollector           │
├─────────────────────────────────────────────────────────────┤
│                Business Logic Layer (Business)              │
│     DeviceManager / SessionManager / HeartbeatManager       │
├─────────────────────────────────────────────────────────────┤
│               Concurrency Scheduling Layer (Concurrency)    │
│          TaskScheduler / ResourceMonitor / AsyncQueue       │
├─────────────────────────────────────────────────────────────┤
│              Communication Adapter Layer (Communication)    │
│         TcpServerChannel / UdpChannel / SerialChannel       │
├─────────────────────────────────────────────────────────────┤
│                Hardware Driver Layer (Driver)               │
│              SocketDriver / SerialPortDriver                │
└─────────────────────────────────────────────────────────────┘
```

## Protocol Parsing

### Supported Protocols

| Protocol Type | Parser | Description |
|---------|--------|-------------|
| Modbus RTU | `ModbusRtuParser` | Serial Modbus protocol with CRC16 check |
| Modbus TCP | `ModbusTcpParser` | TCP Modbus protocol with MBAP header |
| Custom Protocol | `CustomProtocolParser` | Flexible protocol parsing via JSON config |

### Modbus Protocol Parsing

#### Register Types

| Type | Enum Value | Function Codes | Access Permission |
|------|------------|----------------|-------------------|
| Coil | `Coil` | 01/05/0F | Read/Write |
| Discrete Input | `DiscreteInput` | 02 | Read Only |
| Input Register | `InputRegister` | 04 | Read Only |
| Holding Register | `HoldingRegister` | 03/06/10 | Read/Write |

#### JSON Configuration Example

```json
{
  "ProtocolId": "ModbusRtu_001",
  "ProtocolName": "Temperature & Humidity",
  "ModbusType": "Rtu",
  "SlaveId": 1,
  "ByteOrder": "BigEndian",
  "WordOrder": "HighWordFirst",
  "Points": [
    {
      "PointName": "Temperature",
      "RegisterType": "InputRegister",
      "Address": 0,
      "Quantity": 1,
      "DataType": "Int16",
      "Ratio": 0.1,
      "Unit": "℃"
    },
    {
      "PointName": "Humidity",
      "RegisterType": "InputRegister",
      "Address": 1,
      "DataType": "UInt16",
      "Ratio": 0.1,
      "Unit": "%RH"
    },
    {
      "PointName": "RunningStatus",
      "RegisterType": "Coil",
      "Address": 0,
      "DataType": "UInt8"
    }
  ]
}
```

#### Supported Data Types

| Data Type | Byte Count | Description |
|---------|------------|-------------|
| UInt8 / Int8 | 1 | 8-bit integer |
| UInt16 / Int16 | 2 | 16-bit integer |
| UInt32 / Int32 | 4 | 32-bit integer |
| UInt64 / Int64 | 8 | 64-bit integer |
| Float | 4 | Single-precision float |
| Double | 8 | Double-precision float |

### Custom Protocol Parsing

Flexible binary protocol parsing via JSON configuration:

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
      "PointName": "Temperature",
      "Offset": 4,
      "Length": 2,
      "DataType": "UInt16",
      "Ratio": 0.1,
      "Unit": "℃"
    }
  ]
}
```

## CRC Check Utilities

`CrcCalculator` provides multiple check algorithms:

| Algorithm | Method | Usage |
|-----------|--------|-------|
| CRC16 Modbus | `Crc16Modbus()` | Modbus RTU |
| CRC16 CCITT | `Crc16Ccitt()` | Communication protocols |
| CRC32 | `Crc32()` | Ethernet / ZIP |
| CRC8 | `Crc8()` | 8-bit check |
| LRC | `Lrc()` | Modbus ASCII |
| XOR | `XorCheck()` | XOR check |
| Sum | `SumCheck()` | Sum check |

### Usage Example

```csharp
using Vktun.IoT.Connector.Core.Utils;

// CRC16 Modbus
ushort crc = CrcCalculator.Crc16Modbus(data);
byte crcLow = (byte)(crc & 0xFF);
byte crcHigh = (byte)(crc >> 8);

// Verify
bool valid = CrcCalculator.VerifyCrc16Modbus(receivedData, expectedCrc);

// LRC
byte lrc = CrcCalculator.Lrc(data);
```

## Quick Start

### Installation

```bash
dotnet add package Vktun.IoT.Connector
```

## Documentation Index

| Document | Purpose |
| --- | --- |
| [Implementation status and stub notes](Docs/实现状态与桩功能说明.md) | Marks limited, test-only, or demo-only capabilities |
| [New protocol onboarding guide](Docs/新增协议标准接入指南.md) | Standard workflow for parser, template, test, and documentation work |
| [HTTP/MQTT NuGet guide](Docs/HTTP-MQTT通道NuGet使用指南.md) | Registration, device fields, and minimal examples for HTTP/MQTT |
| [NuGet installation matrix](Docs/NuGet安装矩阵与依赖关系.md) | Recommended main package and subpackage installation paths |
| [Protocol template field reference](Docs/协议模板字段说明.md) | Common template fields, point fields, and protocol-specific fields |
| [Device onboarding flow](Docs/新增设备接入流程.md) | End-to-end process from device information to field validation |

### Basic Usage

```csharp
using Vktun.IoT.Connector;
using Vktun.IoT.Connector.Business.Managers;
using Vktun.IoT.Connector.Configuration.Providers;
using Vktun.IoT.Connector.Core.Models;

// Initialize
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

// Configure device
var device = new DeviceInfo
{
    DeviceId = "DEVICE_001",
    DeviceName = "Temperature & Humidity Sensor",
    CommunicationType = CommunicationType.Serial,
    SerialPort = "COM3",
    BaudRate = 9600,
    ProtocolType = ProtocolType.ModbusRtu
};

await collector.AddDeviceAsync(device);
await collector.ConnectDeviceAsync(device.DeviceId);

// Collect data
var data = await collector.CollectDataAsync(device.DeviceId);
```

## Client Tool

The SDK includes a comprehensive client testing tool for industrial protocol testing and debugging.

### Supported Protocols

- **Modbus RTU** - Serial Modbus protocol testing
- **Modbus TCP** - TCP Modbus protocol testing  
- **Siemens S7** - Siemens PLC S7 protocol testing
- **Mitsubishi** - Mitsubishi PLC protocol testing
- **Omron** - Omron PLC protocol testing
- **Serial Port** - General serial communication testing

### Client Screenshots

#### Modbus RTU Client

![Modbus RTU Client](docs/modubsrtu.png)

#### Siemens S7 Client

![Siemens S7 Client](docs/s7.png)

### Features

- Real-time data monitoring and visualization
- Protocol configuration and testing
- Connection status monitoring
- Data logging and analysis
- Multi-protocol support in single interface

## Project Dependencies

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

Vktun.IoT.Connector.Serial (Independent Package)
    └── Vktun.IoT.Connector.Core
```

## Technical Features

- **.NET 10.0**: Latest framework support
- **Async Programming**: async/await + IOCP model
- **Dependency Injection**: Interface decoupling, testable
- **Plugin Design**: Extensible protocol parsers
- **High Concurrency**: Connection pool, thread pool, task queue
- **Cross-platform**: Windows/Linux/macOS

## License

MIT License
