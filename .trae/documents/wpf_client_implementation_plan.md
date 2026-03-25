# WPF客户端项目实施计划

## 项目概述

为 Vktun.IoT.Connector 添加一个基于 WPF 和 Prism 框架的桌面客户端应用程序，参考 IoTClient.Tool 的界面设计，实现 Modbus、串口、西门子、欧姆龙、三菱等工业协议的可视化测试界面。

## 技术栈

* **UI框架**: WPF (.NET 10.0 Windows)

* **MVVM框架**: Prism 9.0.\* (Prism.DryIoc)

* **图表控件**: LiveCharts2 (可选)

* **日志框架**: 使用现有 Vktun.IoT.Connector.Configuration.Logger

* **协议支持**: 复用现有 Vktun.IoT.Connector 模块

## 项目结构

```
src/
└── Vktun.IoT.Connector.Client/
    ├── Vktun.IoT.Connector.Client.csproj          # 主项目文件
    ├── App.xaml                                   # 应用程序入口
    ├── App.xaml.cs
    │
    ├── Models/                                    # 数据模型
    │   ├── ConnectionConfig.cs                    # 连接配置模型
    │   ├── DeviceTestResult.cs                    # 测试结果模型
    │   └── ProtocolTestData.cs                    # 协议测试数据模型
    │
    ├── ViewModels/                                # 视图模型层
    │   ├── MainWindowViewModel.cs                 # 主窗口ViewModel
    │   ├── ModbusTcpViewModel.cs                  # Modbus TCP测试ViewModel
    │   ├── ModbusRtuViewModel.cs                  # Modbus RTU测试ViewModel
    │   ├── SiemensViewModel.cs                    # 西门子PLC测试ViewModel
    │   ├── MitsubishiViewModel.cs                 # 三菱PLC测试ViewModel
    │   ├── OmronViewModel.cs                      # 欧姆龙PLC测试ViewModel
    │   └── SerialPortViewModel.cs                 # 串口测试ViewModel
    │
    ├── Views/                                     # 视图层
    │   ├── MainWindow.xaml                        # 主窗口
    │   ├── ModbusTcpView.xaml                     # Modbus TCP测试界面
    │   ├── ModbusRtuView.xaml                     # Modbus RTU测试界面
    │   ├── SiemensView.xaml                       # 西门子PLC测试界面
    │   ├── MitsubishiView.xaml                    # 三菱PLC测试界面
    │   ├── OmronView.xaml                         # 欧姆龙PLC测试界面
    │   └── SerialPortView.xaml                    # 串口测试界面
    │
    ├── Services/                                  # 服务层
    │   ├── IProtocolTestService.cs                # 协议测试服务接口
    │   ├── ProtocolTestService.cs                 # 协议测试服务实现
    │   ├── IConnectionService.cs                  # 连接服务接口
    │   └── ConnectionService.cs                   # 连接服务实现
    │
    ├── Controls/                                  # 自定义控件
    │   ├── DataGridControl.xaml                   # 数据显示控件
    │   └── LogViewerControl.xaml                  # 日志查看控件
    │
    ├── Converters/                                # 值转换器
    │   ├── BoolToColorConverter.cs                # 布尔转颜色
    │   └── EnumToBooleanConverter.cs              # 枚举转布尔
    │
    └── Resources/                                 # 资源文件
        ├── Styles.xaml                            # 样式定义
        └── Icons/                                 # 图标资源
```

## 实施步骤

### 第一阶段：项目基础架构搭建

#### 1. 创建WPF项目

* 创建 `Vktun.IoT.Connector.Client` WPF项目

* 配置项目文件，添加必要的NuGet包引用：

  * `Prism.DryIoc` (9.0.\*)

  * `Prism.Unity` (可选)

* 设置目标框架为 `net10.0-windows`

* 添加对现有项目的引用：

  * Vktun.IoT.Connector.Core

  * Vktun.IoT.Connector.Protocol

  * Vktun.IoT.Connector.Serial

  * Vktun.IoT.Connector.Configuration

#### 2. 配置Prism框架

* 修改 `App.xaml` 继承自 `PrismApplication`

* 创建 `App.xaml.cs` 配置DryIoc容器

* 注册服务和依赖项

* 配置主窗口和导航框架

#### 3. 创建主窗口布局

* 设计主窗口布局，包含：

  * 左侧导航菜单（协议类型选择）

  * 右侧内容区域（动态加载测试界面）

  * 底部状态栏（连接状态、日志输出）

* 实现Region导航框架

### 第二阶段：协议测试界面开发

#### 4. Modbus TCP测试界面

**功能需求**：

* 连接配置：IP地址、端口、从站ID

* 读写操作：支持读写线圈、离散输入、保持寄存器、输入寄存器

* 数据类型选择：Int16、UInt16、Int32、UInt32、Float、Double

* 批量读写功能

* 实时数据显示

* 通信日志显示

**界面元素**：

* 连接配置区域（IP、端口、从站ID输入框）

* 地址和数据类型选择区域

* 读写按钮区域

* 数据显示DataGrid

* 请求/响应报文显示区域

* 连接状态指示器

#### 5. Modbus RTU测试界面

**功能需求**：

* 串口配置：COM口、波特率、数据位、停止位、校验位

* 支持Modbus RTU协议读写

* 其他功能同Modbus TCP

**界面元素**：

* 串口配置区域

* 其他同Modbus TCP界面

#### 6. 西门子PLC测试界面

**功能需求**：

* 支持S7-200/300/400/1200/1500系列

* 连接配置：IP地址、端口、机架号、槽号

* 支持读写DB块、I/Q/M区

* 地址格式支持：DB1.DBX0.0、DB1.DBD0等

* 数据类型：Bool、Byte、Word、DWord、Real

**界面元素**：

* CPU类型选择（ComboBox）

* 连接配置区域

* 地址输入区域

* 数据类型选择

* 读写操作按钮

* 数据显示区域

#### 7. 三菱PLC测试界面

**功能需求**：

* 支持Qna\_3E、Q\_3E等系列

* 连接配置：IP地址、端口

* 支持读写M、D、X、Y等区域

* 地址格式：M100、D200等

**界面元素**：

* PLC类型选择

* 连接配置区域

* 地址输入区域

* 读写操作区域

#### 8. 欧姆龙PLC测试界面

**功能需求**：

* 支持Fins协议

* 连接配置：IP地址、端口

* 支持读写CIO、DM、WR等区域

* 地址格式支持

**界面元素**：

* 连接配置区域

* 地址输入区域

* 读写操作区域

#### 9. 串口调试界面

**功能需求**：

* 串口参数配置

* 十六进制/字符串发送模式

* 自动发送功能

* 接收数据显示

* 历史记录保存

**界面元素**：

* 串口配置区域

* 发送数据区域（支持Hex/String切换）

* 接收数据区域

* 自动发送配置（间隔时间）

### 第三阶段：服务层实现

#### 10. 实现协议测试服务

* 创建 `IProtocolTestService` 接口

* 实现 `ProtocolTestService` 类，封装：

  * 连接管理

  * 数据读写

  * 错误处理

  * 日志记录

* 集成现有协议解析器：

  * ModbusTcpParser

  * ModbusRtuParser

  * S7ProtocolParser

#### 11. 实现连接服务

* 创建 `IConnectionService` 接口

* 实现 `ConnectionService` 类：

  * 管理连接状态

  * 连接池管理

  * 超时处理

  * 重连机制

### 第四阶段：UI优化和完善

#### 12. 样式和主题

* 创建统一的样式文件 `Styles.xaml`

* 定义颜色主题

* 统一控件样式（按钮、输入框、DataGrid等）

* 添加深色/浅色主题支持（可选）

#### 13. 自定义控件

* 创建数据显示控件 `DataGridControl`

* 创建日志查看控件 `LogViewerControl`

* 创建连接状态指示器控件

#### 14. 值转换器

* 实现 `BoolToColorConverter`（连接状态颜色）

* 实现 `EnumToBooleanConverter`（枚举绑定）

* 其他必要的转换器

### 第五阶段：测试和文档

#### 15. 功能测试

* 测试各协议连接功能

* 测试读写操作

* 测试异常处理

* 测试UI交互

#### 16. 性能优化

* 优化数据刷新频率

* 优化大量数据显示

* 内存泄漏检查

## NuGet包依赖

```xml
<PackageReference Include="Prism.DryIoc" Version="8.1.97" />
<PackageReference Include="Prism.Unity" Version="8.1.97" />
<PackageReference Include="LiveChartsCore.SkiaSharpView.WPF" Version="2.14.10" />
```

## 项目文件配置示例

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Prism.DryIoc" Version="8.1.97" />
    <PackageReference Include="LiveChartsCore.SkiaSharpView.WPF" Version="2.14.10" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Vktun.IoT.Connector.Core\Vktun.IoT.Connector.Core.csproj" />
    <ProjectReference Include="..\Vktun.IoT.Connector.Protocol\Vktun.IoT.Connector.Protocol.csproj" />
    <ProjectReference Include="..\Vktun.IoT.Connector.Serial\Vktun.IoT.Connector.Serial.csproj" />
    <ProjectReference Include="..\Vktun.IoT.Connector.Configuration\Vktun.IoT.Connector.Configuration.csproj" />
  </ItemGroup>
</Project>
```

## 关键技术点

### 1. Prism框架集成

```csharp
// App.xaml.cs
public partial class App : PrismApplication
{
    protected override Window CreateShell()
    {
        return Container.Resolve<MainWindow>();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        // 注册服务
        containerRegistry.RegisterSingleton<IProtocolTestService, ProtocolTestService>();
        containerRegistry.RegisterSingleton<IConnectionService, ConnectionService>();
        
        // 注册导航视图
        containerRegistry.RegisterForNavigation<ModbusTcpView>();
        containerRegistry.RegisterForNavigation<ModbusRtuView>();
        containerRegistry.RegisterForNavigation<SiemensView>();
        // ... 其他视图
    }
}
```

### 2. ViewModel基类

```csharp
public abstract class ProtocolTestViewModelBase : BindableBase, INavigationAware
{
    protected readonly IProtocolTestService _testService;
    protected readonly ILogger _logger;
    
    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }
    
    // 通用连接、读写方法
    public abstract Task ConnectAsync();
    public abstract Task DisconnectAsync();
    public abstract Task ReadAsync();
    public abstract Task WriteAsync();
}
```

### 3. 数据绑定示例

```xml
<!-- ModbusTcpView.xaml -->
<TextBox Text="{Binding IpAddress, UpdateSourceTrigger=PropertyChanged}" 
         Width="150" Margin="5"/>
<ComboBox ItemsSource="{Binding DataTypes}" 
          SelectedItem="{Binding SelectedDataType}"
          Width="100" Margin="5"/>
<Button Content="读取" Command="{Binding ReadCommand}" 
        Width="80" Margin="5"/>
```

## 注意事项

1. **线程安全**: 所有UI操作必须在UI线程执行，使用 `Dispatcher.Invoke` 或 `async/await`
2. **资源释放**: 实现IDisposable接口，确保连接正确关闭
3. **异常处理**: 捕获所有异常并显示友好错误信息
4. **性能考虑**: 大量数据更新时使用虚拟化或分页
5. **日志记录**: 使用统一的日志框架记录操作日志
6. **配置保存**: 使用JSON文件保存用户配置（连接参数等）

## 预期成果

1. 完整的WPF桌面应用程序，支持多种工业协议测试
2. 模块化设计，易于扩展新协议
3. 友好的用户界面，参考IoTClient.Tool的设计风格
4. 完善的错误处理和日志记录
5. 可保存和加载测试配置

## 后续扩展

1. 添加更多PLC品牌支持（AB、贝加莱等）
2. 添加数据监控和图表显示
3. 添加数据导出功能（Excel、CSV）
4. 添加脚本自动化测试功能
5. 添加远程监控功能

