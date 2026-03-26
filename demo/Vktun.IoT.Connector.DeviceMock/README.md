# Vktun.IoT.Connector.DeviceMock

工业设备模拟器项目，## 项目概述

本项目用于模拟各种工业设备（Modbus、西门子S7、三菱、欧姆龙等），支持多种通讯方式（TCP、UDP、串口）和协议，为客户端测试和开发提供模拟环境。

## 功能特性

### 已实现功能

- **Modbus TCP服务器**
  - 支持所有标准功能码（01/02/03/04/05/06/0F/10）
  - 支持多客户端连接
  - 支持线圈、离散输入、输入寄存器、保持寄存器
  - 支持数据模拟（静态、随机、正弦、线性、阶跃）

### 计划实现功能

- **西门子S7服务器**
  - 支持S7-200/300/400/1200/1500系列
  - 支持读写DB块、I/Q/M区
  
- **三菱MC协议服务器**
  - 支持Qna_3E、Q_3E等系列
  - 支持读写M、D、X、Y等区域
  
- **欧姆龙FINS服务器**
  - 支持FINS协议
  - 支持读写CIO、DM、WR等区域

## 项目结构

```
Vktun.IoT.Connector.DeviceMock/
├── Models/                      # 数据模型
│   ├── MockDeviceConfig.cs      # 设备配置模型
│   ├── MockDataPoint.cs         # 数据点模型
│   └── DeviceMockConfig.cs      # 配置文件模型
│
├── Services/                   # 服务层
│   ├── IDeviceSimulator.cs     # 设备模拟器接口
│   ├── DataSimulator.cs        # 数据模拟服务
│   └── DeviceManager.cs        # 设备管理器
│
├── Protocols/                  # 协议实现
│   ├── Modbus/                 # Modbus协议
│   │   ├── ModbusTcpServer.cs   # Modbus TCP服务器
│   │   └── ModbusDataStore.cs   # Modbus数据存储
│   │
│   ├── Siemens/                # 西门子S7协议（计划中）
│   │   ├── S7Server.cs
│   │   ├── S7DataBlockManager.cs
│   │   └── S7ProtocolHandler.cs
│   │
│   ├── Mitsubishi/             # 三菱MC协议（计划中）
│   │   ├── MitsubishiServer.cs
│   │   └── MitsubishiDataStore.cs
│   │
│   └── Omron/                  # 欧姆龙FINS协议（计划中）
│       ├── OmronFinsServer.cs
│       └── OmronDataStore.cs
│
├── Communication/               # 通信层
│   ├── TcpServerBase.cs        # TCP服务器基类
│   ├── UdpServerBase.cs        # UDP服务器基类
│   └── SerialPortSimulator.cs  # 串口模拟器（计划中）
│
├── Config/                     # 配置文件
│   └── device_config.json      # 设备配置示例
│
└── Program.cs                  # 主程序入口
```

## 快速开始

### 1. 配置设备

编辑 `Config/device_config.json` 文件，配置要模拟的设备：

```json
{
  "devices": [
    {
      "deviceId": "MODBUS_TCP_001",
      "deviceName": "Modbus TCP模拟设备",
      "protocolType": "ModbusTcp",
      "communicationType": "Tcp",
      "port": 502,
      "slaveId": 1,
      "enabled": true,
      "dataPoints": [
        {
          "pointName": "温度",
          "address": "40001",
          "dataType": "Float",
          "simulationType": "Sine",
          "minValue": 20.0,
          "maxValue": 30.0,
          "updateInterval": 1000
        }
      ]
    }
  ]
}
```

### 2. 运行程序

```bash
cd demo/Vktun.IoT.Connector.DeviceMock
dotnet run
```

### 3. 测试连接

使用Modbus客户端工具（如Modbus Poll、IoTClient.Tool等）连接到 `localhost:502` 进行测试。

## 数据模拟类型

- **Static**: 静态值，保持不变
- **Random**: 随机值，在最小值和最大值之间随机变化
- **Sine**: 正弦波，按正弦规律变化
- **Linear**: 线性变化，从最小值到最大值循环
- **Step**: 阶跃变化，在最小值和最大值之间切换

## Modbus地址映射

- **00001-09999**: 线圈（Coils）
- **10001-19999**: 离散输入（Discrete Inputs）
- **30001-39999**: 输入寄存器（Input Registers）
- **40001-49999**: 保持寄存器（Holding Registers）

## 开发计划

### 第一阶段 ✅
- [x] 项目基础架构
- [x] 数据模型层
- [x] 服务层
- [x] 通信层基类
- [x] Modbus TCP服务器

### 第二阶段 🚧
- [ ] 西门子S7服务器
- [ ] 三菱MC服务器
- [ ] 欧姆龙FINS服务器

### 第三阶段 📋
- [ ] Web管理界面
- [ ] 数据记录和回放
- [ ] 性能监控

## 技术栈

- **.NET 10.0**
- **TCP/UDP/串口通信**
- **JSON配置**
- **异步编程**

## 注意事项

1. **端口权限**: Modbus TCP默认使用502端口，可能需要管理员权限
2. **防火墙**: 确保防火墙允许相应端口的通信
3. **数据类型**: 注意不同协议支持的数据类型差异
4. **并发处理**: 服务器支持多客户端并发连接

## 测试工具推荐

- **Modbus Poll/Slave**: Modbus协议测试工具
- **IoTClient.Tool**: 开源IoT协议测试工具
- **NetToPLCSim**: S7协议测试工具
- **自定义客户端**: 使用Vktun.IoT.Connector.Client进行测试

## 许可证

MIT License

## 贡献

欢迎贡献代码、报告问题或提出建议！
