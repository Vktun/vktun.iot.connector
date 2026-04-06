using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Business.Services;
using Vktun.IoT.Connector.Business.Cloud;
using Vktun.IoT.Connector.Communication.Channels;
using Vktun.IoT.Connector.Protocol.Parsers;
using Vktun.IoT.Connector.Configuration.Logging;
using Vktun.IoT.Connector.Configuration.Providers;

namespace Vktun.IoT.Connector.Examples;

/// <summary>
/// Vktun.IoT.Connector API参考Demo示例
/// 展示如何使用SDK的各种功能进行工业设备数据采集
/// </summary>
public class ApiReferenceDemo
{
    private readonly ILogger _logger;

    public ApiReferenceDemo(ILogger logger)
    {
        _logger = logger ?? new SerilogLogger();
    }

    /// <summary>
    /// 示例1：使用MQTT通道连接云端
    /// </summary>
    public async Task Example1_MqttChannelAsync()
    {
        _logger.Info("=== 示例1: MQTT通道使用 ===");

        // 创建MQTT配置
        var mqttConfig = new MqttChannelConfig
        {
            BrokerAddress = "mqtt-broker.example.com",
            Port = 1883,
            ClientId = "vktun_connector_001",
            Username = "device_user",
            Password = "device_password",
            CleanSession = true,
            KeepAliveInterval = 60,
            EnableSsl = false
        };

        // 创建MQTT通道
        using var mqttChannel = new MqttChannel(_logger, mqttConfig);

        // 连接服务器
        await mqttChannel.ConnectAsync("mqtt_device_001");

        // 订阅主题
        await mqttChannel.SubscribeAsync(new[]
        {
            new MqttSubscription { Topic = "devices/001/commands", QosLevel = 1 },
            new MqttSubscription { Topic = "devices/001/config", QosLevel = 1 }
        });

        // 发布数据
        var telemetry = new DeviceData
        {
            DeviceId = "device_001",
            Timestamp = DateTime.UtcNow,
            DataPoints = new List<DataPoint>
            {
                new DataPoint { PointName = "temperature", Value = 25.5, Quality = "Good" },
                new DataPoint { PointName = "humidity", Value = 60.2, Quality = "Good" }
            }
        };

        await mqttChannel.PublishAsync("devices/001/telemetry", telemetry);

        // 接收消息
        var receivedData = await mqttChannel.ReceiveAsync("mqtt_device_001", CancellationToken.None);
        foreach (var data in receivedData)
        {
            _logger.Info($"Received: {data.DeviceId}, Points: {data.DataPoints.Count}");
        }

        // 断开连接
        await mqttChannel.DisconnectAsync("mqtt_device_001");

        _logger.Info("MQTT示例完成");
    }

    /// <summary>
    /// 示例2：连接Azure IoT Hub
    /// </summary>
    public async Task Example2_AzureIoTHubAsync()
    {
        _logger.Info("=== 示例2: Azure IoT Hub集成 ===");

        // Azure IoT Hub配置
        var azureConfig = new AzureIoTHubConfig
        {
            DeviceId = "vktun_device_001",
            IotHubConnectionString = "HostName=vktun-hub.azure-devices.net;DeviceId=vktun_device_001;SharedAccessKey=xxx",
            Protocol = "Mqtt",
            TransportProtocol = "Mqtt_Tcp_Only",
            RetryPolicy = "ExponentialBackoff",
            OperationTimeout = 60000
        };

        // 创建Azure IoT Hub连接器
        using var azureConnector = new AzureIoTHubConnector(_logger, azureConfig);

        // 连接
        await azureConnector.ConnectAsync();

        // 发送遥测数据
        var telemetryData = new Dictionary<string, object>
        {
            ["temperature"] = 26.5,
            ["pressure"] = 101.3,
            ["humidity"] = 55.8,
            ["deviceStatus"] = "Running"
        };

        await azureConnector.SendTelemetryAsync(telemetryData);

        // 更新Device Twin报告属性
        var reportedProperties = new Dictionary<string, object>
        {
            ["firmwareVersion"] = "1.2.3",
            ["lastUpdateTime"] = DateTime.UtcNow.ToString("O"),
            ["deviceHealth"] = "OK"
        };

        await azureConnector.UpdateTwinReportedPropertiesAsync(reportedProperties);

        // 获取Device Twin期望属性
        var desiredProperties = await azureConnector.GetTwinDesiredPropertiesAsync();
        _logger.Info($"Desired properties: {desiredProperties.Count} items");

        // 处理云端直接方法调用
        azureConnector.RegisterDirectMethodHandler("SetConfiguration", async (methodName, payload) =>
        {
            _logger.Info($"Direct method received: {methodName}");
            _logger.Info($"Payload: {payload}");

            // 处理配置更新
            // payload包含新的配置参数...

            return new Dictionary<string, object>
            {
                ["result"] = "success",
                ["timestamp"] = DateTime.UtcNow.ToString("O")
            };
        });

        // 接收云端到设备(C2D)消息
        await azureConnector.ReceiveC2DMessageAsync();

        // 断开连接
        await azureConnector.DisconnectAsync();

        _logger.Info("Azure IoT Hub示例完成");
    }

    /// <summary>
    /// 示例3：连接AWS IoT Core/Greengrass
    /// </summary>
    public async Task Example3_AwsIoTAsync()
    {
        _logger.Info("=== 示例3: AWS IoT Core/Greengrass集成 ===");

        // AWS IoT配置
        var awsConfig = new AwsIoTConfig
        {
            DeviceId = "vktun_device_001",
            Endpoint = "xxxxxx-ats.iot.us-east-1.amazonaws.com",
            Region = "us-east-1",
            ThingName = "vktun_thing_001",
            CertificatePath = "/certs/device.crt",
            PrivateKeyPath = "/certs/device.key",
            CaCertificatePath = "/certs/root-ca.pem",
            ClientId = "vktun_greengrass_client"
        };

        // 创建AWS IoT连接器
        using var awsConnector = new AwsIoTConnector(_logger, awsConfig);

        // 连接
        await awsConnector.ConnectAsync();

        // 发布遥测数据
        var telemetryData = new Dictionary<string, object>
        {
            ["temperature"] = 27.3,
            ["pressure"] = 102.5,
            ["flowRate"] = 15.6,
            ["timestamp"] = DateTime.UtcNow.ToString("O")
        };

        await awsConnector.PublishTelemetryAsync("vktun/device/001/telemetry", telemetryData);

        // 更新Device Shadow报告状态
        var reportedState = new Dictionary<string, object>
        {
            ["operatingMode"] = "Auto",
            ["setpoint"] = 25.0,
            ["lastHeartbeat"] = DateTime.UtcNow.ToString("O"),
            ["connectionStatus"] = "Online"
        };

        await awsConnector.UpdateDeviceShadowReportedAsync(reportedState);

        // 获取Device Shadow期望状态
        var shadowState = await awsConnector.GetDeviceShadowAsync();
        _logger.Info($"Shadow state: {shadowState}");

        // 监听Shadow变更
        awsConnector.SubscribeToDeviceShadowDeltaAsync((delta) =>
        {
            _logger.Info($"Shadow delta received: {delta.Count} changes");
            foreach (var kvp in delta)
            {
                _logger.Info($"  {kvp.Key}: {kvp.Value}");
            }
        });

        // 发布Greengrass本地消息（用于边缘计算）
        await awsConnector.PublishGreengrassAsync("local/analytics/input", new Dictionary<string, object>
        {
            ["rawData"] = telemetryData,
            ["processingType"] = "AnomalyDetection"
        });

        // 接收Greengrass Lambda输出
        awsConnector.SubscribeGreengrassOutputAsync("local/analytics/output", (output) =>
        {
            _logger.Info($"Lambda output: {output}");
        });

        // 断开连接
        await awsConnector.DisconnectAsync();

        _logger.Info("AWS IoT示例完成");
    }

    /// <summary>
    /// 示例4：OPC UA协议设备通信
    /// </summary>
    public async Task Example4_OpcUaAsync()
    {
        _logger.Info("=== 示例4: OPC UA协议通信 ===");

        // OPC UA协议配置
        var opcuaConfig = new ProtocolConfig
        {
            ProtocolId = "opcua-v1",
            ProtocolName = "OPC UA",
            ProtocolType = ProtocolType.Custom,
            AdditionalSettings = new Dictionary<string, object?>
            {
                ["EndpointUrl"] = "opc.tcp://192.168.1.100:4840",
                ["SecurityMode"] = "SignAndEncrypt",
                ["SecurityPolicy"] = "Basic256Sha256",
                ["Username"] = "admin",
                ["Password"] = "password",
                ["PublishingInterval"] = 1000
            }
        };

        // 创建OPC UA协议解析器
        var opcuaParser = new OpcUaProtocolParser(_logger);

        // 创建读取请求
        var nodeIds = new[]
        {
            "ns=2;s=Device.Temperature",
            "ns=2;s=Device.Pressure",
            "ns=2;s=Device.Status"
        };

        var readRequest = OpcUaProtocolParser.CreateReadRequest(nodeIds);

        // 发送请求（通过通信通道）...
        // byte[] response = await channel.SendAsync(readRequest);

        // 解析响应（示例数据）
        byte[] sampleResponse = CreateOpcUaSampleResponse();
        var parsedData = opcuaParser.Parse(sampleResponse, opcuaConfig);

        foreach (var deviceData in parsedData)
        {
            _logger.Info($"Device: {deviceData.DeviceId}, Time: {deviceData.Timestamp}");
            foreach (var point in deviceData.DataPoints)
            {
                _logger.Info($"  {point.PointName}: {point.Value} [{point.Quality}]");
            }
        }

        // 创建写入请求
        var writeValues = new Dictionary<string, object>
        {
            ["ns=2;s=Device.Setpoint"] = 50.0,
            ["ns=2;s=Device.RunMode"] = true
        };

        var writeRequest = OpcUaProtocolParser.CreateWriteRequest(writeValues);

        // 创建订阅请求
        var subscribeRequest = OpcUaProtocolParser.CreateSubscribeRequest(nodeIds, 1000);

        // 解析节点ID
        var (nsIndex, identifier, idType) = OpcUaProtocolParser.ParseNodeId("ns=2;s=MyDevice.Temperature");
        _logger.Info($"Parsed NodeId: ns={nsIndex}, id={identifier}, type={idType}");

        _logger.Info("OPC UA示例完成");
    }

    /// <summary>
    /// 示例5：BACnet楼宇自动化设备通信
    /// </summary>
    public async Task Example5_BacnetAsync()
    {
        _logger.Info("=== 示例5: BACnet楼宇自动化通信 ===");

        // BACnet配置
        var bacnetConfig = new ProtocolConfig
        {
            ProtocolId = "bacnet-v1",
            ProtocolName = "BACnet/IP",
            ProtocolType = ProtocolType.Custom,
            AdditionalSettings = new Dictionary<string, object?>
            {
                ["DeviceId"] = 1234,
                ["Port"] = 47808,
                ["MaxApduLength"] = 1476
            }
        };

        // 创建BACnet协议解析器
        var bacnetParser = new BacnetProtocolParser(_logger);

        // 创建Who-Is广播请求（发现设备）
        var whoIsRequest = BacnetProtocolParser.CreateWhoIsRequest(0, 4194303);

        // 发送Who-Is广播...
        // await udpChannel.SendAsync(whoIsRequest);

        // 创建读取属性请求
        var readRequest = BacnetProtocolParser.CreateReadPropertyRequest(
            deviceInstance: 1234,
            objectType: BacnetObjectType.AnalogInput,
            objectInstance: 1,
            propertyId: BacnetPropertyId.PresentValue
        );

        // 发送读取请求...
        // byte[] response = await channel.SendAsync(readRequest);

        // 解析响应（示例数据）
        byte[] sampleResponse = CreateBacnetSampleResponse();
        var parsedData = bacnetParser.Parse(sampleResponse, bacnetConfig);

        foreach (var deviceData in parsedData)
        {
            _logger.Info($"Device: {deviceData.DeviceId}");
            foreach (var point in deviceData.DataPoints)
            {
                _logger.Info($"  {point.PointName}: {point.Value}");
            }
        }

        // 创建写入属性请求
        var writeRequest = BacnetProtocolParser.CreateWritePropertyRequest(
            deviceInstance: 1234,
            objectType: BacnetObjectType.AnalogOutput,
            objectInstance: 1,
            propertyId: BacnetPropertyId.PresentValue,
            value: 75.0,
            priority: 16
        );

        // 创建COV订阅请求（值变更通知）
        var subscribeRequest = BacnetProtocolParser.CreateSubscribeCOVRequest(
            subscriberProcessId: 1,
            deviceInstance: 1234,
            objectType: BacnetObjectType.AnalogInput,
            objectInstance: 1,
            confirmedNotifications: true,
            lifetime: 3600
        );

        // 解析对象标识符
        uint objectId = BacnetProtocolParser.ParseObjectIdentifier("AI:1");
        BacnetObjectType objType = BacnetProtocolParser.ParseObjectType("AV");
        _logger.Info($"Object ID: {objectId}, Type: {objType}");

        _logger.Info("BACnet示例完成");
    }

    /// <summary>
    /// 示例6：CANopen工业总线设备通信
    /// </summary>
    public async Task Example6_CanopenAsync()
    {
        _logger.Info("=== 示例6: CANopen工业总线通信 ===");

        // CANopen配置
        var canopenConfig = new ProtocolConfig
        {
            ProtocolId = "canopen-v1",
            ProtocolName = "CANopen",
            ProtocolType = ProtocolType.Custom,
            AdditionalSettings = new Dictionary<string, object?>
            {
                ["NodeId"] = 1,
                ["BaudRate"] = 250000,
                ["HeartbeatTimeout"] = 1000,
                ["EnableSync"] = true,
                ["SyncPeriod"] = 100
            }
        };

        // 创建CANopen协议解析器
        var canopenParser = new CanopenProtocolParser(_logger);

        // 创建NMT命令帧 - 设置节点为运行状态
        var nmtStartFrame = CanopenProtocolParser.CreateNmtCommandFrame(CanopenNmtCommand.Operational, nodeId: 1);

        // 创建NMT命令帧 - 重置节点
        var nmtResetFrame = CanopenProtocolParser.CreateNmtCommandFrame(CanopenNmtCommand.ResetNode, nodeId: 1);

        // 发送NMT命令...
        // await canChannel.SendAsync(nmtStartFrame);

        // 创建同步帧（SYNC）
        var syncFrame = CanopenProtocolParser.CreateSyncFrame();

        // 定期发送SYNC消息触发PDO传输
        // while (running) {
        //     await canChannel.SendAsync(syncFrame);
        //     await Task.Delay(100);
        // }

        // 创建SDO读取请求（读取设备类型）
        var sdoReadFrame = CanopenProtocolParser.CreateSdoReadRequestFrame(
            index: 0x1000,
            subIndex: 0,
            nodeId: 1
        );

        // 创建SDO写入请求（设置心跳时间）
        var sdoWriteFrame = CanopenProtocolParser.CreateSdoWriteRequestFrame(
            index: 0x1017,
            subIndex: 0,
            value: 1000,
            dataType: CanopenDataType.Unsigned16,
            nodeId: 1
        );

        // 发送SDO请求...
        // byte[] response = await canChannel.SendAsync(sdoReadFrame);

        // 解析CAN帧响应（示例数据）
        byte[] sampleResponse = CreateCanopenSampleResponse();
        var parsedData = canopenParser.Parse(sampleResponse, canopenConfig);

        foreach (var deviceData in parsedData)
        {
            _logger.Info($"Device: {deviceData.DeviceId}");
            foreach (var point in deviceData.DataPoints)
            {
                _logger.Info($"  {point.PointName}: {point.Value} [{point.Quality}]");
            }
        }

        // 解析对象索引
        var objIndex = CanopenProtocolParser.ParseObjectIndex("6040:0");
        _logger.Info($"Object Index: {objIndex.Index:X4}:{objIndex.SubIndex}, Type: {objIndex.DataType}");

        // 创建设备命令
        var readCommand = new DeviceCommand
        {
            CommandType = "SDO_Read",
            Parameters = new Dictionary<string, object?>
            {
                ["Index"] = 0x6041,
                ["SubIndex"] = 0
            }
        };

        var commandFrame = canopenParser.Pack(readCommand, canopenConfig);

        _logger.Info("CANopen示例完成");
    }

    /// <summary>
    /// 示例7：安全认证和数据加密
    /// </summary>
    public async Task Example7_SecurityAsync()
    {
        _logger.Info("=== 示例7: 安全认证和加密 ===");

        // 创建认证配置
        var authConfig = new AuthenticationConfig
        {
            EnableAuthentication = true,
            ApiKeyHeader = "X-Api-Key",
            TokenHeader = "X-Auth-Token",
            TokenExpiryMinutes = 60,
            EnableIpFilter = true,
            AllowedIpRanges = new List<string> { "192.168.1.0/24", "10.0.0.0/8" },
            EnableRateLimit = true,
            RateLimitPerMinute = 100
        };

        // 创建认证服务
        var authService = new AuthenticationService(_logger, authConfig);

        // 添加API Key
        var apiKey = new ApiKeyCredential
        {
            KeyId = "device_key_001",
            KeyValue = "sk_vktun_abc123def456",
            DeviceId = "device_001",
            Permissions = new List<string> { "read", "write", "subscribe" },
            ExpiryDate = DateTime.UtcNow.AddDays(30),
            IsActive = true
        };

        authService.AddApiKey(apiKey);

        // 验证认证请求
        var authRequest = new AuthenticationRequest
        {
            ApiKey = "sk_vktun_abc123def456",
            IpAddress = "192.168.1.100",
            DeviceId = "device_001"
        };

        var authResult = await authService.AuthenticateAsync(authRequest);
        if (authResult.IsSuccess)
        {
            _logger.Info($"Authentication successful: Token={authResult.Token}");
            _logger.Info($"Permissions: {string.Join(", ", authResult.Permissions)}");
        }
        else
        {
            _logger.Warning($"Authentication failed: {authResult.ErrorMessage}");
        }

        // 创建TLS配置
        var tlsConfig = new TlsConfig
        {
            EnableTls = true,
            CertificatePath = "/certs/server.crt",
            PrivateKeyPath = "/certs/server.key",
            CaCertificatePath = "/certs/ca.crt",
            RequireClientCertificate = true,
            MinTlsVersion = "1.2",
            AllowedCipherSuites = new List<string> { "TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384" }
        };

        // 创建证书管理器
        var certManager = new CertificateManager(_logger, tlsConfig);

        // 加载证书
        var certificate = certManager.LoadCertificate();
        if (certificate != null)
        {
            _logger.Info($"Certificate loaded: {certificate.Subject}");
            _logger.Info($"Valid until: {certificate.NotAfter}");
        }

        // 验证客户端证书
        var clientCert = certManager.LoadCertificate("/certs/client.crt");
        bool isValid = certManager.ValidateCertificate(clientCert);
        _logger.Info($"Client certificate valid: {isValid}");

        // 创建安全TCP通道
        var secureChannelConfig = new SecureChannelConfig
        {
            EnableTls = true,
            ServerCertificate = certificate,
            RequireClientCertificate = true,
            ValidateClientCertificate = true
        };

        // using var secureChannel = new SecureTcpChannel(_logger, secureChannelConfig);
        // await secureChannel.ConnectAsync("secure_device_001");

        _logger.Info("安全认证示例完成");
    }

    /// <summary>
    /// 示例8：设备状态机和重试策略
    /// </summary>
    public async Task Example8_StateMachineAndRetryAsync()
    {
        _logger.Info("=== 示例8: 设备状态机和重试策略 ===");

        // 创建设备状态机
        var stateMachine = new DeviceStateMachine("device_001", _logger);

        // 注册状态变更回调
        stateMachine.OnStateChanged += (sender, args) =>
        {
            _logger.Info($"Device {sender.DeviceId}: {args.OldStatus} -> {args.NewStatus}");
        };

        // 状态转换操作
        stateMachine.Connect();      // Offline -> Connecting
        stateMachine.Connected();    // Connecting -> Online
        stateMachine.Disconnect();   // Online -> Disconnecting
        stateMachine.Disconnected(); // Disconnecting -> Offline

        // 模拟错误场景
        stateMachine.Connect();
        stateMachine.ErrorOccurred("Connection timeout", true);  // Connecting -> Error
        _logger.Info($"Current state: {stateMachine.CurrentState}");
        _logger.Info($"Error count: {stateMachine.ErrorCount}");

        // 自动恢复
        stateMachine.TryAutoRecover(); // Error -> Connecting (if errors < max)
        _logger.Info($"After recovery: {stateMachine.CurrentState}");

        // 创建重试策略
        var retryPolicy = new RetryPolicy(_logger, new RetryPolicyConfig
        {
            MaxRetries = 5,
            InitialDelayMs = 1000,
            MaxDelayMs = 60000,
            BackoffMultiplier = 2.0,
            JitterFactor = 0.3,
            RetryableExceptionTypes = new List<string> { "TimeoutException", "SocketException" }
        });

        // 使用重试策略执行操作
        int attemptCount = 0;
        var result = await retryPolicy.ExecuteAsync(async () =>
        {
            attemptCount++;
            _logger.Info($"Attempt {attemptCount}");

            // 模拟失败场景
            if (attemptCount < 3)
            {
                throw new TimeoutException("Connection timeout");
            }

            // 第三次尝试成功
            return await Task.FromResult("Operation completed successfully");
        });

        _logger.Info($"Final result: {result}");

        // 检查是否应该放弃
        bool shouldGiveUp = retryPolicy.ShouldGiveUp(new TimeoutException(), 6);
        _logger.Info($"Should give up after 6 failures: {shouldGiveUp}");

        _logger.Info("状态机和重试策略示例完成");
    }

    /// <summary>
    /// 示例9：Serilog结构化日志
    /// </summary>
    public void Example9_SerilogLogging()
    {
        _logger.Info("=== 示例9: Serilog结构化日志 ===");

        // 创建Serilog日志配置
        var logConfig = new LogConfiguration
        {
            MinimumLevel = SerilogLogLevel.Debug,
            WriteToConsole = true,
            WriteToFile = true,
            FilePath = "/logs/vktun_connector.log",
            FileRollingInterval = "Day",
            FileRetentionDays = 30,
            OutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
            EnrichWithMachineName = true,
            EnrichWithThreadId = true
        };

        // 创建Serilog日志器
        var serilogLogger = new SerilogLogger(logConfig);

        // 使用结构化日志记录设备数据
        var deviceData = new DeviceData
        {
            DeviceId = "device_001",
            Timestamp = DateTime.UtcNow,
            DataPoints = new List<DataPoint>
            {
                new DataPoint { PointName = "temperature", Value = 25.5, Quality = "Good" }
            }
        };

        // 结构化日志（使用属性）
        serilogLogger.Info("Device data received: {DeviceId}, {PointCount} points, Timestamp={Timestamp}",
            deviceData.DeviceId, deviceData.DataPoints.Count, deviceData.Timestamp);

        // 记录性能指标
        serilogLogger.Info("Data collection completed: {DurationMs}ms, {BytesReceived} bytes, {DeviceCount} devices",
            150, 1024, 5);

        // 记录错误（带异常）
        try
        {
            throw new InvalidOperationException("Simulated error");
        }
        catch (Exception ex)
        {
            serilogLogger.Error("Operation failed: {Operation}, Error={ErrorMessage}", "DataRead", ex.Message, ex);
        }

        // 更新日志级别
        serilogLogger.SetLogLevel(SerilogLogLevel.Warning);
        serilogLogger.Debug("This debug message will be filtered"); // 低于Warning级别，不输出

        serilogLogger.Info("Serilog日志示例完成");
    }

    /// <summary>
    /// 示例10：完整数据采集流程
    /// </summary>
    public async Task Example10_CompleteWorkflowAsync()
    {
        _logger.Info("=== 示例10: 完整数据采集流程 ===");

        // 1. 初始化日志系统
        var logConfig = new LogConfiguration
        {
            MinimumLevel = SerilogLogLevel.Info,
            WriteToConsole = true,
            WriteToFile = true,
            FilePath = "/logs/connector.log"
        };
        var logger = new SerilogLogger(logConfig);

        // 2. 配置安全认证
        var authConfig = new AuthenticationConfig
        {
            EnableAuthentication = true,
            TokenExpiryMinutes = 60
        };
        var authService = new AuthenticationService(logger, authConfig);

        // 3. 创建通信通道
        var mqttConfig = new MqttChannelConfig
        {
            BrokerAddress = "mqtt.example.com",
            Port = 1883,
            ClientId = "vktun_connector"
        };
        using var mqttChannel = new MqttChannel(logger, mqttConfig);

        // 4. 创建协议解析器
        var opcuaParser = new OpcUaProtocolParser(logger);
        var bacnetParser = new BacnetProtocolParser(logger);
        var canopenParser = new CanopenProtocolParser(logger);

        // 5. 创建云端连接器
        var azureConfig = new AzureIoTHubConfig
        {
            DeviceId = "edge_device_001",
            IotHubConnectionString = "HostName=..."
        };
        using var azureConnector = new AzureIoTHubConnector(logger, azureConfig);

        // 6. 创建设备状态机
        var stateMachine = new DeviceStateMachine("edge_device_001", logger);

        // 7. 创建重试策略
        var retryPolicy = new RetryPolicy(logger, new RetryPolicyConfig
        {
            MaxRetries = 3,
            InitialDelayMs = 1000
        });

        // 8. 执行数据采集循环
        stateMachine.Connect();
        try
        {
            // 连接MQTT
            await retryPolicy.ExecuteAsync(async () =>
            {
                await mqttChannel.ConnectAsync("edge_device_001");
            });

            // 连接Azure IoT Hub
            await retryPolicy.ExecuteAsync(async () =>
            {
                await azureConnector.ConnectAsync();
            });

            stateMachine.Connected();

            // 数据采集循环
            while (stateMachine.CurrentState == DeviceStatus.Online)
            {
                // 从OPC UA设备采集数据
                var opcuaData = await CollectOpcUaDataAsync(opcuaParser, mqttConfig);

                // 从BACnet设备采集数据
                var bacnetData = await CollectBacnetDataAsync(bacnetParser, mqttConfig);

                // 从CANopen设备采集数据
                var canopenData = await CollectCanopenDataAsync(canopenParser, mqttConfig);

                // 合并数据
                var allData = opcuaData.Concat(bacnetData).Concat(canopenData).ToList();

                // 发布到MQTT
                foreach (var data in allData)
                {
                    await mqttChannel.PublishAsync($"telemetry/{data.DeviceId}", data);
                }

                // 发送到Azure IoT Hub
                var telemetryDict = new Dictionary<string, object>();
                foreach (var data in allData)
                {
                    foreach (var point in data.DataPoints)
                    {
                        telemetryDict[$"{data.DeviceId}_{point.PointName}"] = point.Value;
                    }
                }
                await azureConnector.SendTelemetryAsync(telemetryDict);

                // 等待下一个采集周期
                await Task.Delay(5000);
            }
        }
        catch (Exception ex)
        {
            stateMachine.ErrorOccurred(ex.Message, true);
            logger.Error($"Data collection failed: {ex.Message}", ex);
        }
        finally
        {
            stateMachine.Disconnect();
            await mqttChannel.DisconnectAsync("edge_device_001");
            await azureConnector.DisconnectAsync();
            stateMachine.Disconnected();
        }

        logger.Info("完整数据采集流程示例完成");
    }

    // 辅助方法：创建示例响应数据
    private byte[] CreateOpcUaSampleResponse()
    {
        var result = new List<byte>();

        // 节点ID: ns=2;s=Device.Temperature
        var nodeId = "ns=2;s=Device.Temperature";
        var nodeIdBytes = Encoding.UTF8.GetBytes(nodeId);
        result.AddRange(BitConverter.GetBytes((ushort)nodeIdBytes.Length));
        result.AddRange(nodeIdBytes);

        // 数据类型: Float (9)
        result.Add(9);

        // 数据值: 25.5
        result.AddRange(BitConverter.GetBytes(25.5f));

        // 质量: Good (192)
        result.Add(192);

        // 时间戳
        result.AddRange(BitConverter.GetBytes(DateTime.UtcNow.Ticks));

        return result.ToArray();
    }

    private byte[] CreateBacnetSampleResponse()
    {
        var result = new List<byte>();

        // BVLC版本
        result.Add(0x01);
        // BVLC类型
        result.Add(0x04);
        // BVLC长度
        result.Add(0x00);
        result.Add(0x14);

        // NPDU版本
        result.Add(0x01);
        // NPDU控制
        result.Add(0x00);

        // APDU - Complex ACK
        result.Add(0x20); // Complex ACK PDU
        result.Add(0x00); // Invoke ID
        result.Add(0x0C); // ReadProperty ACK

        // 对象标识符 (AI:1)
        uint objectId = ((uint)BacnetObjectType.AnalogInput << 22) | 1;
        result.AddRange(BitConverter.GetBytes(objectId));

        // 属性标识符
        result.Add((byte)BacnetPropertyId.PresentValue);

        // 开放标签
        result.Add(0x3E);

        // 值 (Real: 24.5)
        result.AddRange(BitConverter.GetBytes(24.5f));

        // 关闭标签
        result.Add(0x3F);

        return result.ToArray();
    }

    private byte[] CreateCanopenSampleResponse()
    {
        var result = new List<byte>();

        // 帧头
        result.Add(0xAA);

        // CAN ID - PDO1 TX (节点ID=1)
        uint canId = 0x180 + 1;
        result.AddRange(BitConverter.GetBytes(canId));

        // DLC
        result.Add(8);

        // PDO数据 (温度: 26.5°C = 0x41D40000 as float, 状态: 0x05)
        result.AddRange(BitConverter.GetBytes(26.5f)); // 4 bytes
        result.Add(0x05); // Status byte
        result.AddRange(new byte[] { 0x00, 0x00, 0x00 }); // Padding

        return result.ToArray();
    }

    // 辅助方法：模拟数据采集
    private async Task<List<DeviceData>> CollectOpcUaDataAsync(OpcUaProtocolParser parser, MqttChannelConfig config)
    {
        await Task.Delay(100); // 模拟采集延迟
        return parser.Parse(CreateOpcUaSampleResponse(), new ProtocolConfig());
    }

    private async Task<List<DeviceData>> CollectBacnetDataAsync(BacnetProtocolParser parser, MqttChannelConfig config)
    {
        await Task.Delay(100);
        return parser.Parse(CreateBacnetSampleResponse(), new ProtocolConfig());
    }

    private async Task<List<DeviceData>> CollectCanopenDataAsync(CanopenProtocolParser parser, MqttChannelConfig config)
    {
        await Task.Delay(100);
        return parser.Parse(CreateCanopenSampleResponse(), new ProtocolConfig());
    }
}

/// <summary>
/// 主程序入口
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        var logger = new SerilogLogger(new LogConfiguration
        {
            MinimumLevel = SerilogLogLevel.Info,
            WriteToConsole = true
        });

        var demo = new ApiReferenceDemo(logger);

        Console.WriteLine("Vktun.IoT.Connector API参考示例");
        Console.WriteLine("================================");
        Console.WriteLine("选择示例:");
        Console.WriteLine("1 - MQTT通道使用");
        Console.WriteLine("2 - Azure IoT Hub集成");
        Console.WriteLine("3 - AWS IoT Core/Greengrass集成");
        Console.WriteLine("4 - OPC UA协议通信");
        Console.WriteLine("5 - BACnet楼宇自动化");
        Console.WriteLine("6 - CANopen工业总线");
        Console.WriteLine("7 - 安全认证和加密");
        Console.WriteLine("8 - 设备状态机和重试策略");
        Console.WriteLine("9 - Serilog结构化日志");
        Console.WriteLine("10 - 完整数据采集流程");
        Console.WriteLine("0 - 运行所有示例");
        Console.Write("请输入选项: ");

        var input = Console.ReadLine();

        try
        {
            switch (input)
            {
                case "1":
                    await demo.Example1_MqttChannelAsync();
                    break;
                case "2":
                    await demo.Example2_AzureIoTHubAsync();
                    break;
                case "3":
                    await demo.Example3_AwsIoTAsync();
                    break;
                case "4":
                    await demo.Example4_OpcUaAsync();
                    break;
                case "5":
                    await demo.Example5_BacnetAsync();
                    break;
                case "6":
                    await demo.Example6_CanopenAsync();
                    break;
                case "7":
                    await demo.Example7_SecurityAsync();
                    break;
                case "8":
                    await demo.Example8_StateMachineAndRetryAsync();
                    break;
                case "9":
                    demo.Example9_SerilogLogging();
                    break;
                case "10":
                    await demo.Example10_CompleteWorkflowAsync();
                    break;
                case "0":
                    await demo.Example1_MqttChannelAsync();
                    await demo.Example2_AzureIoTHubAsync();
                    await demo.Example3_AwsIoTAsync();
                    await demo.Example4_OpcUaAsync();
                    await demo.Example5_BacnetAsync();
                    await demo.Example6_CanopenAsync();
                    await demo.Example7_SecurityAsync();
                    await demo.Example8_StateMachineAndRetryAsync();
                    demo.Example9_SerilogLogging();
                    await demo.Example10_CompleteWorkflowAsync();
                    break;
                default:
                    Console.WriteLine("无效选项");
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.Error($"示例执行失败: {ex.Message}", ex);
        }

        Console.WriteLine("按任意键退出...");
        Console.ReadKey();
    }
}