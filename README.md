# Vktun.IoT.Connector

Industrial device data acquisition SDK with a DI-based runtime facade, communication channels, protocol parsers, templates, and demo tooling. The currently verified paths focus on Modbus RTU/TCP, HTTP/MQTT, and custom protocol scenarios; other protocol claims should be evaluated against the maturity notes in the docs.

**Language**: [English](README.md) | [中文](README.zh.md)

## Current Capability Snapshot

| Area | Status | Notes |
| --- | --- | --- |
| Facade + DI entry | Verified | `IIoTDataCollector`, `AddVktunIoTConnector`, `AddVktunHttpChannel`, and `AddVktunMqttChannel` have build/test coverage. |
| TCP / UDP / Serial channels | Limited usable | Real send/receive chains are integrated, but real device validation is still required. |
| HTTP / MQTT channels | Verified for SDK integration | Minimal documentation, DI registration, and tests are present; production still depends on environment health checks. |
| Modbus RTU / TCP | Verified | Parser, packing, template compatibility, and regression tests are present. |
| Custom protocol | Limited usable | JSON-driven parsing is available; field validation still depends on real sample frames. |
| S7 | Limited usable | Use specialized command APIs rather than the generic `BuildRequest` stub entry. |
| IEC104 | Limited usable | Parser entry exists, but command modeling remains limited for full ASDU control scenarios. |
| OPC UA / BACnet / CANopen | Experimental | Present in code, currently routed through `ProtocolType.Custom`, and not production-ready. |
| Azure / AWS cloud connectors | Opt-in only | Explicit DI registrations are available for targeted integration, but they are not part of the default `AddVktunIoTConnector` runtime path. |
| DeviceMock | Development only | Suitable for development and regression, not a production gateway service. |

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

## Production Notes

- The repo currently targets `net10.0` and the verified build/test baseline uses the preview SDK.
- Check [Implementation status and stub notes](Docs/实现状态与桩功能说明.md) before promising protocol scope externally.
- `UseInMemoryTransport` is only for tests and local development.
- `DeviceMock` is a development/regression tool, not a production gateway service.
- `OPC UA`, `BACnet`, and `CANopen` are experimental in the current codebase.

### Basic Usage

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

// Configure device
var device = new DeviceInfo
{
    DeviceId = "DEVICE_001",
    DeviceName = "Temperature & Humidity Sensor",
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

// Collect data
var data = await collector.CollectDataAsync(device.DeviceId);
```

## Client Tool

The SDK includes a WPF client workbench for protocol debugging and demo scenarios.

### Current Workbench Scope

- **Verified paths**: Modbus RTU, Modbus TCP, Siemens S7, and serial debugging.
- **Demo-level pages**: Mitsubishi and Omron pages exist in the client, but they are not covered by the current DeviceMock regression loop or documented as production-ready.

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

- **.NET 10.0**: Current target framework, verified with the preview SDK baseline in this repo
- **Async Programming**: async/await + IOCP model
- **Dependency Injection**: Interface decoupling, testable
- **Plugin Design**: Extensible protocol parsers
- **High Concurrency**: Connection pool, thread pool, task queue
- **Cross-platform**: Windows/Linux/macOS

## License

MIT License
