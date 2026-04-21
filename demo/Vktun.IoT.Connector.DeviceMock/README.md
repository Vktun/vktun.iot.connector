# Vktun.IoT.Connector.DeviceMock

工业设备模拟器项目，当前已实现 Modbus TCP 和西门子 S7 两条主要模拟链路，并提供数据记录/回放与性能监控能力，用于客户端测试、开发联调和回归环境。

> 当前已实现：Modbus TCP、Siemens S7、数据记录/回放、性能监控。三菱 MC、欧姆龙 FINS 和 Web 管理界面仍在计划中。DeviceMock 不是生产网关服务。

## 功能特性

### 已实现功能

#### 1. Modbus TCP服务器 ✅
- 支持所有标准功能码（01/02/03/04/05/06/0F/10）
- 支持多客户端连接
- 支持线圈、离散输入、输入寄存器、保持寄存器
- 支持数据模拟（静态、随机、正弦、线性、阶跃）

#### 2. 西门子S7服务器 ✅
- 支持S7-200/300/400/1200/1500系列
- 支持读写DB块（DB1-DB65535）
- 支持读写I/Q/M区
- 支持多种数据类型（Bit, Byte, Word, DWord, Real）
- 支持TPKT和ISO-on-TCP协议
- 支持多客户端连接

#### 3. 数据记录和回放功能 ✅
- 基于SQLite数据库的数据存储
- 支持多设备同时记录
- 支持按时间范围查询
- 支持数据回放速度控制
- 支持数据导出（CSV、Excel、JSON）

#### 4. 性能监控和统计功能 ✅
- 实时性能监控（CPU、内存、线程）
- 连接数统计
- 请求响应时间统计
- 数据吞吐量统计
- 错误率统计
- 性能报告生成

### 计划实现功能

- **三菱MC协议服务器**
  - 支持Qna_3E、Q_3E等系列
  - 支持读写M、D、X、Y等区域
  
- **欧姆龙FINS服务器**
  - 支持FINS协议
  - 支持读写CIO、DM、WR等区域

## 项目结构

```
Vktun.IoT.Connector.DeviceMock/
├── Models/                              # 数据模型
│   ├── MockDeviceConfig.cs              # 设备配置模型
│   ├── MockDataPoint.cs                 # 数据点模型
│   └── DeviceMockConfig.cs              # 配置文件模型
│
├── Services/                            # 服务层
│   ├── IDeviceSimulator.cs              # 设备模拟器接口
│   ├── DataSimulator.cs                 # 数据模拟服务
│   ├── DeviceManager.cs                 # 设备管理器
│   │
│   ├── Recording/                       # 数据记录
│   │   ├── DataRecordingService.cs      # 数据记录服务
│   │   ├── DataRecorder.cs              # 数据记录器
│   │   └── DataPlayer.cs                # 数据回放器
│   │
│   └── Monitoring/                      # 性能监控
│       └── PerformanceMonitor.cs        # 性能监控器
│
├── Protocols/                           # 协议实现
│   ├── Modbus/                          # Modbus协议
│   │   ├── ModbusTcpServer.cs           # Modbus TCP服务器
│   │   └── ModbusDataStore.cs           # Modbus数据存储
│   │
│   └── Siemens/                         # 西门子S7协议
│       ├── S7Server.cs                  # S7服务器
│       ├── S7DataBlockManager.cs        # DB块管理器
│       └── S7ProtocolHandler.cs         # S7协议处理器
│
├── Communication/                       # 通信层
│   ├── TcpServerBase.cs                 # TCP服务器基类
│   └── UdpServerBase.cs                 # UDP服务器基类
│
├── Config/                              # 配置文件
│   └── device_config.json               # 设备配置示例
│
└── Program.cs                           # 主程序入口
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

- **Modbus TCP**: 使用Modbus客户端工具连接到 `localhost:502`
- **S7**: 使用S7客户端工具连接到 `localhost:102`

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

## S7地址格式

- **DB块**: `DB1.DBW0` (DB1的字地址0), `DB1.DBX0.0` (DB1的位地址0.0)
- **I区**: `I0.0` (位), `IW0` (字)
- **Q区**: `Q0.0` (位), `QW0` (字)
- **M区**: `M0.0` (位), `MW0` (字)

## 数据记录和回放

### 开始记录

```csharp
var recorder = new DataRecorder(logger, recordingService);
await recorder.StartRecordingAsync("DEVICE_001", dataPoints, "测试记录");
```

### 停止记录

```csharp
await recorder.StopRecordingAsync();
```

### 回放数据

```csharp
var player = new DataPlayer(logger, recordingService);
await player.LoadSessionAsync(sessionId);
player.StartPlayback((address, value) =>
{
    Console.WriteLine($"{address}: {value}");
});
```

## 性能监控

### 启动监控

```csharp
var monitor = new PerformanceMonitor(logger);
monitor.StartMonitoring(1000); // 每秒更新一次
```

### 记录性能数据

```csharp
monitor.IncrementCounter("Requests");
monitor.RecordDuration("ResponseTime", duration);
```

### 生成报告

```csharp
var report = monitor.GenerateReport();
Console.WriteLine($"平均CPU使用率: {report.AverageCpuUsage:F2}%");
Console.WriteLine($"平均内存使用: {report.AverageMemoryUsage:F2} MB");
```

## 开发计划

### 第一阶段 ✅
- [x] 项目基础架构
- [x] 数据模型层
- [x] 服务层
- [x] 通信层基类
- [x] Modbus TCP服务器

### 第二阶段 ✅
- [x] 西门子S7服务器
- [x] 数据记录和回放功能
- [x] 性能监控和统计功能

### 第三阶段 🚧
- [ ] 三菱MC服务器
- [ ] 欧姆龙FINS服务器
- [ ] Web管理界面

## 技术栈

- **.NET 10.0**
- **TCP/UDP/串口通信**
- **SQLite数据库**
- **JSON配置**
- **异步编程**

## 注意事项

1. **端口权限**: Modbus TCP默认使用502端口，S7默认使用102端口，可能需要管理员权限
2. **防火墙**: 确保防火墙允许相应端口的通信
3. **数据类型**: 注意不同协议支持的数据类型差异
4. **并发处理**: 服务器支持多客户端并发连接
5. **性能**: 建议在性能监控下运行，避免资源耗尽

## 测试工具推荐

- **Modbus Poll/Slave**: Modbus协议测试工具
- **IoTClient.Tool**: 开源IoT协议测试工具
- **NetToPLCSim**: S7协议测试工具
- **自定义客户端**: 使用Vktun.IoT.Connector.Client进行测试

## 许可证

MIT License

## 贡献

欢迎贡献代码、报告问题或提出建议！
