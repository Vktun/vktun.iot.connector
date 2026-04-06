using System.Text;
using System.Text.Json;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Business.Cloud;

/// <summary>
/// AWS IoT配置
/// </summary>
public class AwsIoTConfig
{
    /// <summary>
    /// AWS区域
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// IoT端点
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// 设备/事物名称
    /// </summary>
    public string ThingName { get; set; } = string.Empty;

    /// <summary>
    /// 证书路径
    /// </summary>
    public string CertificatePath { get; set; } = string.Empty;

    /// <summary>
    /// 私钥路径
    /// </summary>
    public string PrivateKeyPath { get; set; } = string.Empty;

    /// <summary>
    /// CA证书路径
    /// </summary>
    public string RootCAPath { get; set; } = string.Empty;

    /// <summary>
    /// MQTT端口
    /// </summary>
    public int Port { get; set; } = 8883;

    /// <summary>
    /// 是否启用Greengrass本地模式
    /// </summary>
    public bool EnableGreengrass { get; set; } = false;

    /// <summary>
    /// Greengrass核心端点
    /// </summary>
    public string? GreengrassCoreEndpoint { get; set; }
}

/// <summary>
/// AWS IoT设备影子状态
/// </summary>
public class DeviceShadowState
{
    public Dictionary<string, object> Desired { get; set; } = new();
    public Dictionary<string, object> Reported { get; set; } = new();
    public Dictionary<string, object>? Delta { get; set; }
}

/// <summary>
/// AWS IoT Greengrass连接器 - 支持Shadow同步、Lambda触发、IPC通信
/// </summary>
public class AwsIoTConnector : IAsyncDisposable
{
    private readonly AwsIoTConfig _config;
    private readonly ILogger _logger;
    private bool _isConnected;
    private DeviceShadowState _shadowState = new();
    private Timer? _syncTimer;

    public event EventHandler<Dictionary<string, object>>? ShadowDeltaReceived;
    public event EventHandler<DeviceShadowState>? ShadowUpdated;
    public event EventHandler<string>? LambdaTriggered;

    public AwsIoTConnector(AwsIoTConfig config, ILogger logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 连接到AWS IoT Core/Greengrass
    /// </summary>
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = _config.EnableGreengrass && !string.IsNullOrEmpty(_config.GreengrassCoreEndpoint)
                ? _config.GreengrassCoreEndpoint
                : _config.Endpoint;

            _logger.Info($"Connecting to AWS IoT: {endpoint}, Thing: {_config.ThingName}");

            // TODO: 使用AWS SDK连接
            // var mqttClient = new AmazonIotMqttClient(
            //     _config.Endpoint,
            //     _config.Port,
            //     _config.CertificatePath,
            //     _config.PrivateKeyPath,
            //     _config.RootCAPath);

            _isConnected = true;

            // 订阅Shadow更新主题
            await SubscribeShadowTopicsAsync(cancellationToken);

            // 获取初始Shadow状态
            await GetShadowAsync(cancellationToken);

            _logger.Info("Successfully connected to AWS IoT");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to connect to AWS IoT: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public async Task DisconnectAsync()
    {
        _syncTimer?.Dispose();
        _syncTimer = null;
        _isConnected = false;

        _logger.Info("Disconnected from AWS IoT");
        await Task.CompletedTask;
    }

    /// <summary>
    /// 发布遥测数据
    /// </summary>
    public async Task PublishTelemetryAsync(string topic, Dictionary<string, object> data, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            _logger.Warning("Not connected to AWS IoT");
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                thingName = _config.ThingName,
                timestamp = DateTime.UtcNow.ToString("o"),
                data = data
            });

            var fullTopic = topic.StartsWith("$aws/things/") 
                ? topic 
                : $"{_config.ThingName}/telemetry";

            _logger.Debug($"Publishing to {fullTopic}: {payload}");

            // TODO: 使用AWS SDK发布
            // await _mqttClient.PublishAsync(fullTopic, payload);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to publish telemetry: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 发送设备数据
    /// </summary>
    public async Task SendDeviceDataAsync(DeviceData data, CancellationToken cancellationToken = default)
    {
        var telemetry = new Dictionary<string, object>();
        foreach (var point in data.DataItems)
        {
            telemetry[point.PointName] = point.Value;
        }

        await PublishTelemetryAsync("data", telemetry, cancellationToken);
    }

    /// <summary>
    /// 获取设备影子
    /// </summary>
    public async Task<DeviceShadowState> GetShadowAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            _logger.Warning("Not connected to AWS IoT");
            return _shadowState;
        }

        try
        {
            _logger.Debug("Getting device shadow...");

            // TODO: 使用AWS SDK获取影子
            // var response = await _iotClient.GetThingShadowAsync(new GetThingShadowRequest
            // {
            //     ThingName = _config.ThingName
            // }, cancellationToken);

            return _shadowState;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get device shadow: {ex.Message}", ex);
            return _shadowState;
        }
    }

    /// <summary>
    /// 更新设备影子报告状态
    /// </summary>
    public async Task UpdateShadowReportedAsync(Dictionary<string, object> properties, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            _logger.Warning("Not connected to AWS IoT");
            return;
        }

        try
        {
            foreach (var kvp in properties)
            {
                _shadowState.Reported[kvp.Key] = kvp.Value;
            }

            var payload = JsonSerializer.Serialize(new
            {
                state = new
                {
                    reported = properties
                }
            });

            _logger.Debug($"Updating shadow reported: {payload}");

            // TODO: 使用AWS SDK更新影子
            // var request = new UpdateThingShadowRequest
            // {
            //     ThingName = _config.ThingName,
            //     Payload = Encoding.UTF8.GetBytes(payload)
            // };
            // await _iotClient.UpdateThingShadowAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to update shadow reported: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 触发Greengrass Lambda函数
    /// </summary>
    public async Task TriggerLambdaAsync(string functionName, object payload, CancellationToken cancellationToken = default)
    {
        if (!_isConnected || !_config.EnableGreengrass)
        {
            _logger.Warning("Not connected to Greengrass or Greengrass not enabled");
            return;
        }

        try
        {
            var topic = $"lambda/{functionName}/invoke";
            var message = JsonSerializer.Serialize(new
            {
                requestId = Guid.NewGuid().ToString(),
                payload = payload,
                timestamp = DateTime.UtcNow
            });

            _logger.Debug($"Triggering Lambda {functionName}: {message}");

            // TODO: 使用Greengrass IPC触发Lambda
            // await _greengrassClient.PublishToTopicAsync(topic, message);

            LambdaTriggered?.Invoke(this, functionName);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to trigger Lambda: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 订阅Shadow主题
    /// </summary>
    private async Task SubscribeShadowTopicsAsync(CancellationToken cancellationToken)
    {
        // 订阅Shadow更新确认
        var acceptedTopic = $"$aws/things/{_config.ThingName}/shadow/update/accepted";
        var rejectedTopic = $"$aws/things/{_config.ThingName}/shadow/update/rejected";
        var deltaTopic = $"$aws/things/{_config.ThingName}/shadow/update/delta";

        _logger.Debug($"Subscribing to shadow topics...");

        // TODO: 使用MQTT客户端订阅
        // await _mqttClient.SubscribeAsync(acceptedTopic);
        // await _mqttClient.SubscribeAsync(rejectedTopic);
        // await _mqttClient.SubscribeAsync(deltaTopic);

        await Task.CompletedTask;
    }

    /// <summary>
    /// 处理Shadow Delta更新
    /// </summary>
    public void HandleShadowDelta(Dictionary<string, object> delta)
    {
        _shadowState.Delta = delta;
        ShadowDeltaReceived?.Invoke(this, delta);
        _logger.Info($"Shadow delta received: {JsonSerializer.Serialize(delta)}");
    }

    /// <summary>
    /// 定期同步Shadow状态
    /// </summary>
    public void StartShadowSync(int intervalMs = 30000)
    {
        _syncTimer = new Timer(async _ => await SyncShadowAsync(), null,
            TimeSpan.FromMilliseconds(intervalMs),
            TimeSpan.FromMilliseconds(intervalMs));
    }

    private async Task SyncShadowAsync()
    {
        if (!_isConnected)
            return;

        try
        {
            var currentState = await GetShadowAsync();
            ShadowUpdated?.Invoke(this, currentState);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to sync shadow: {ex.Message}", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}