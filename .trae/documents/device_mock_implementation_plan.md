# 设备模拟器项目实施计划

## 项目概述

创建 `Vktun.IoT.Connector.DeviceMock` 项目，用于模拟各种工业设备（Modbus、西门子S7、三菱、欧姆龙等），支持多种通讯方式（TCP、UDP、串口）和协议，为客户端测试提供模拟环境。

## 技术栈

* **运行框架**: .NET 10.0

* **通信方式**: TCP、UDP、串口

* **协议支持**: Modbus TCP/RTU、S7、三菱MC、欧姆龙FINS

* **配置方式**: JSON配置文件

* **日志框架**: 使用现有 Vktun.IoT.Connector.Configuration.Logger

## 项目结构

```
demo/
└── Vktun.IoT.Connector.DeviceMock/
    ├── Vktun.IoT.Connector.DeviceMock.csproj    # 主项目文件
    ├── Program.cs                                # 主程序入口
    │
    ├── Models/                                    # 数据模型
    │   ├── MockDeviceConfig.cs                    # 设备配置模型
    │   ├── MockDataPoint.cs                       # 数据点模型
    │   └── DataSimulationType.cs                  # 数据模拟类型枚举
    │
    ├── Services/                                  # 服务层
    │   ├── IDeviceSimulator.cs                    # 设备模拟器接口
    │   ├── DataSimulator.cs                       # 数据模拟服务
    │   └── DeviceManager.cs                       # 设备管理器
    │
    ├── Protocols/                                 # 协议实现
    │   ├── Modbus/                                # Modbus协议
    │   │   ├── ModbusTcpServer.cs                 # Modbus TCP服务器
    │   │   ├── ModbusRtuSlave.cs                  # Modbus RTU从站
    │   │   └── ModbusDataStore.cs                 # Modbus数据存储
    │   │
    │   ├── Siemens/                               # 西门子S7协议
    │   │   ├── S7Server.cs                        # S7服务器
    │   │   ├── S7DataBlockManager.cs              # DB块管理器
    │   │   └── S7ProtocolHandler.cs               # S7协议处理器
    │   │
    │   ├── Mitsubishi/                            # 三菱MC协议
    │   │   ├── MitsubishiServer.cs                # 三菱服务器
    │   │   └── MitsubishiDataStore.cs             # 三菱数据存储
    │   │
    │   └── Omron/                                 # 欧姆龙FINS协议
    │       ├── OmronFinsServer.cs                 # 欧姆龙服务器
    │       └── OmronDataStore.cs                  # 欧姆龙数据存储
    │
    ├── Communication/                             # 通信层
    │   ├── TcpServerBase.cs                       # TCP服务器基类
    │   ├── UdpServerBase.cs                       # UDP服务器基类
    │   └── SerialPortSimulator.cs                 # 串口模拟器
    │
    └── Config/                                    # 配置文件
        ├── device_config.json                     # 设备配置
        ├── modbus_config.json                     # Modbus配置
        ├── s7_config.json                         # S7配置
        └── data_simulation.json                   # 数据模拟配置
```

## 实施步骤

### 第一阶段：项目基础架构

#### 1. 创建项目文件

* 创建控制台项目 `Vktun.IoT.Connector.DeviceMock`

* 配置项目文件，目标框架 `net10.0`

* 添加NuGet包：

  * `System.IO.Ports` (8.0.0) - 串口支持

  * `System.Text.Json` (8.0.0) - JSON配置

* 添加项目引用：

  * Vktun.IoT.Connector.Core

  * Vktun.IoT.Connector.Protocol

#### 2. 创建数据模型

**MockDeviceConfig.cs**:

```csharp
public class MockDeviceConfig
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public ProtocolType ProtocolType { get; set; }
    public CommunicationType CommunicationType { get; set; }
    public int Port { get; set; }
    public bool Enabled { get; set; } = true;
    public List<MockDataPoint> DataPoints { get; set; } = new();
}
```

**MockDataPoint.cs**:

```csharp
public class MockDataPoint
{
    public string PointName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public DataType DataType { get; set; } = DataType.UInt16;
    public object? InitialValue { get; set; }
    public DataSimulationType SimulationType { get; set; } = DataSimulationType.Static;
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public double UpdateInterval { get; set; } = 1000; // ms
}

public enum DataSimulationType
{
    Static,         // 静态值
    Random,         // 随机值
    Sine,           // 正弦波
    Linear,         // 线性变化
    Step            // 阶跃变化
}
```

#### 3. 创建服务层

**IDeviceSimulator.cs**:

```csharp
public interface IDeviceSimulator
{
    string DeviceId { get; }
    ProtocolType ProtocolType { get; }
    bool IsRunning { get; }
    
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
    void SetDataPoint(string address, object value);
    object GetDataPoint(string address);
}
```

**DataSimulator.cs**:

* 实现数据模拟逻辑

* 支持多种数据变化模式

* 定时更新模拟数据

### 第二阶段：Modbus协议实现

#### 4. Modbus TCP服务器

**功能需求**:

* 支持功能码：

  * 0x01: 读线圈

  * 0x02: 读离散输入

  * 0x03: 读保持寄存器

  * 0x04: 读输入寄存器

  * 0x05: 写单个线圈

  * 0x06: 写单个寄存器

  * 0x0F: 写多个线圈

  * 0x10: 写多个寄存器

* 支持多客户端连接

* 异常响应处理（功能码0x80+原功能码）

**实现要点**:

* TCP监听端口（默认502）

* MBAP头解析（TransactionId、ProtocolId、Length、UnitId）

* PDU处理（FunctionCode、Data）

* 数据存储管理

#### 5. Modbus RTU从站

**功能需求**:

* 串口通信模拟

* RTU帧格式处理

* CRC16校验

* 从站ID过滤

**实现要点**:

* 串口监听

* RTU帧解析和响应

* 数据存储与Modbus TCP共享

#### 6. Modbus数据存储

**ModbusDataStore.cs**:

```csharp
public class ModbusDataStore
{
    public bool[] Coils { get; set; }                      // 线圈 (地址 00001-09999)
    public bool[] DiscreteInputs { get; set; }             // 离散输入 (地址 10001-19999)
    public ushort[] InputRegisters { get; set; }           // 输入寄存器 (地址 30001-39999)
    public ushort[] HoldingRegisters { get; set; }         // 保持寄存器 (地址 40001-49999)
    
    public void Initialize(int coilCount, int discreteInputCount, 
                          int inputRegisterCount, int holdingRegisterCount);
    public void SetCoil(ushort address, bool value);
    public bool GetCoil(ushort address);
    public void SetHoldingRegister(ushort address, ushort value);
    public ushort GetHoldingRegister(ushort address);
}
```

### 第三阶段：西门子S7协议实现

#### 7. S7服务器

**功能需求**:

* 支持S7-200/300/400/1200/1500系列

* 支持读写DB块、I/Q/M区

* TPKT和ISO-on-TCP协议

* PDU处理

**实现要点**:

* TCP监听端口（默认102）

* 连接建立和协商

* S7协议帧解析

* 数据块管理

#### 8. S7数据块管理器

**S7DataBlockManager.cs**:

```csharp
public class S7DataBlockManager
{
    public Dictionary<int, byte[]> DataBlocks { get; set; }  // DB块数据
    public byte[] Inputs { get; set; }                        // I区
    public byte[] Outputs { get; set; }                       // Q区
    public byte[] Merkers { get; set; }                       // M区
    
    public void Initialize(int dbCount, int dbSize);
    public void SetBit(string address, bool value);           // 如: DB1.DBX0.0
    public void SetWord(string address, ushort value);        // 如: DB1.DBW0
    public void SetDWord(string address, uint value);         // 如: DB1.DBD0
    public void SetReal(string address, float value);         // 如: DB1.DBD0
}
```

### 第四阶段：三菱和欧姆龙协议实现

#### 9. 三菱MC协议服务器

**功能需求**:

* 支持Qna\_3E、Q\_3E等系列

* 支持读写M、D、X、Y等区域

* MC协议帧格式

* 二进制和ASCII模式

**实现要点**:

* TCP监听端口（默认6000）

* MC协议帧解析

* 地址映射和数据存储

#### 10. 欧姆龙FINS协议服务器

**功能需求**:

* 支持FINS协议

* 支持读写CIO、DM、WR等区域

* UDP/TCP通信

* 命令响应处理

**实现要点**:

* UDP监听端口（默认9600）

* FINS帧解析

* 地址映射和数据存储

### 第五阶段：通信层和主程序

#### 11. TCP服务器基类

**TcpServerBase.cs**:

```csharp
public abstract class TcpServerBase
{
    protected TcpListener? _listener;
    protected CancellationTokenSource? _cts;
    protected List<TcpClient> _clients = new();
    
    public abstract Task StartAsync(int port, CancellationToken cancellationToken);
    public abstract Task StopAsync();
    protected abstract Task HandleClientAsync(TcpClient client);
}
```

#### 12. UDP服务器基类

**UdpServerBase.cs**:

```csharp
public abstract class UdpServerBase
{
    protected UdpClient? _udpServer;
    protected CancellationTokenSource? _cts;
    
    public abstract Task StartAsync(int port, CancellationToken cancellationToken);
    public abstract Task StopAsync();
    protected abstract Task HandleMessageAsync(byte[] data, IPEndPoint remoteEP);
}
```

#### 13. 串口模拟器

**SerialPortSimulator.cs**:

* 模拟串口设备

* 支持虚拟串口对（com0com等）

* 数据收发处理

#### 14. 主程序

**Program.cs**:

```csharp
class Program
{
    static async Task Main(string[] args)
    {
        // 1. 加载配置
        // 2. 创建设备模拟器
        // 3. 启动服务
        // 4. 运行数据模拟
        // 5. 等待退出信号
    }
}
```

### 第六阶段：配置和测试

#### 15. 配置文件

**device\_config.json**:

```json
{
  "devices": [
    {
      "deviceId": "MODBUS_TCP_001",
      "deviceName": "Modbus TCP模拟设备",
      "protocolType": "ModbusTcp",
      "communicationType": "Tcp",
      "port": 502,
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

#### 16. 功能测试

* 测试Modbus TCP服务器连接和读写

* 测试S7服务器连接和读写

* 测试三菱服务器连接和读写

* 测试欧姆龙服务器连接和读写

* 测试数据模拟功能

## 关键技术点

### 1. Modbus TCP帧格式

```
MBAP Header (7 bytes):
- Transaction Identifier (2 bytes)
- Protocol Identifier (2 bytes) = 0x0000
- Length (2 bytes)
- Unit Identifier (1 byte)

PDU:
- Function Code (1 byte)
- Data (variable)
```

### 2. S7协议帧格式

```
TPKT Header (4 bytes):
- Version (1 byte) = 0x03
- Reserved (1 byte) = 0x00
- Length (2 bytes)

ISO-on-TCP Header (3 bytes):
- Length (1 byte)
- PDU Type (1 byte)
- Destination Reference (2 bytes)
- Source Reference (2 bytes)
- Class/Option (1 byte)

S7 PDU:
- Header (10 bytes)
- Parameters (variable)
- Data (variable)
```

### 3. 数据模拟算法

```csharp
public class DataSimulator
{
    public static double GenerateValue(DataSimulationType type, 
                                       double min, double max, 
                                       double time, double period)
    {
        return type switch
        {
            DataSimulationType.Static => min,
            DataSimulationType.Random => Random.Shared.NextDouble() * (max - min) + min,
            DataSimulationType.Sine => (Math.Sin(time * 2 * Math.PI / period) + 1) / 2 * (max - min) + min,
            DataSimulationType.Linear => (time % period) / period * (max - min) + min,
            DataSimulationType.Step => time % (period * 2) < period ? min : max,
            _ => min
        };
    }
}
```

## 项目文件配置

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="System.IO.Ports" Version="8.0.0" />
    <PackageReference Include="System.Text.Json" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Vktun.IoT.Connector.Core\Vktun.IoT.Connector.Core.csproj" />
    <ProjectReference Include="..\..\src\Vktun.IoT.Connector.Protocol\Vktun.IoT.Connector.Protocol.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="Config\**\*.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

## 预期成果

1. **完整的设备模拟器**：支持Modbus、S7、三菱、欧姆龙等主流协议
2. **灵活的数据模拟**：支持静态、随机、正弦、线性等多种数据变化模式
3. **易于配置**：通过JSON配置文件管理设备和数据点
4. **独立运行**：可作为独立服务运行，为客户端提供测试环境

## 测试工具

* **IoTClient.Tool**: 可用于测试模拟器功能

* **Modbus Poll/Slave**: Modbus协议测试工具

* **NetToPLCSim**: S7协议测试工具

* **自定义客户端**: 使用Vktun.IoT.Connector.Client进行测试

## 后续扩展

1. 添加更多协议支持（AB、贝加莱等）
2. 添加数据记录和回放功能
3. 添加WPF管理界面
4. 添加脚本自动化测试功能
5. 添加性能监控和统计功能

