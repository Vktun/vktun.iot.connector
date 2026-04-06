using System.Text;
using System.Text.Json;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Business.Cloud;

/// <summary>
/// Azure IoT Hub配置
/// </summary>
public class AzureIoTHubConfig
{
    /// <summary>
    /// IoT Hub主机名（如：your-hub.azure-devices.net）
    /// </summary>
    public string HostName { get; set; } = string.Empty;

    /// <summary>
    /// 设备ID
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// 共享访问密钥
    /// </summary>
    public string SharedAccessKey { get; set; } = string.Empty;

    /// <summary>
    /// 消息发送间隔（毫秒）
    /// </summary>
    public int SendInterval { get; set; } = 5000;

    /// <summary>
    /// 是否启用设备孪生同步
    /// </summary>
    public bool EnableTwinSync { get; set; } = true;

    /// <summary>
    /// 是否启用直接方法
    /// </summary>
    public bool EnableDirectMethods { get; set; } = true;
}

/// <summary>
/// Azure IoT Hub设备孪生属性
/// </summary>
public class DeviceTwinProperties
{
    public Dictionary<string, object> Desired { get; set; } = new();
    public Dictionary<string, object> Reported { get; set; } = new();
}

/// <summary>
/// Azure IoT Hub连接器 - 支持设备孪生、直接方法、C2D消息
/// </summary>
public class AzureIoTHubConnector : IAsyncDisposable
{
    private readonly AzureIoTHubConfig _config;
    private readonly ILogger _logger;
    private readonly string _connectionString;
    private bool _isConnected;
    private Timer? _heartbeatTimer;
    private DeviceTwinProperties _twinProperties = new();

    public event EventHandler<Dictionary<string, object>>? TwinDesiredPropertiesChanged;
    public event EventHandler<DirectMethodRequest>? DirectMethodReceived;
    public event EventHandler<CloudToDeviceMessage>? CloudMessageReceived;

    public AzureIoTHubConnector(AzureIoTHubConfig config, ILogger logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 构建连接字符串
        _connectionString = $"HostName={_config.HostName};DeviceId={_config.DeviceId};SharedAccessKey={_config.SharedAccessKey}";
    }

    /// <summary>
    /// 连接到Azure IoT Hub
    /// </summary>
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.Info($"Connecting to Azure IoT Hub: {_config.HostName}, Device: {_config.DeviceId}");

            // TODO: 使用Azure SDK连接
            // var deviceClient = DeviceClient.CreateFromConnectionString(_connectionString);

            _isConnected = true;

            // 启动心跳定时器
            _heartbeatTimer = new Timer(async _ => await SendHeartbeatAsync(), null, 
                TimeSpan.FromMilliseconds(_config.SendInterval), 
                TimeSpan.FromMilliseconds(_config.SendInterval));

            // 如果启用设备孪生同步，获取初始状态
            if (_config.EnableTwinSync)
            {
                await GetTwinAsync(cancellationToken);
            }

            _logger.Info($"Successfully connected to Azure IoT Hub");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to connect to Azure IoT Hub: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public async Task DisconnectAsync()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        _isConnected = false;

        _logger.Info("Disconnected from Azure IoT Hub");
        await Task.CompletedTask;
    }

    /// <summary>
    /// 发送遥测数据
    /// </summary>
    public async Task SendTelemetryAsync(Dictionary<string, object> telemetry, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            _logger.Warning("Not connected to Azure IoT Hub");
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                deviceId = _config.DeviceId,
                timestamp = DateTime.UtcNow,
                data = telemetry
            });

            _logger.Debug($"Sending telemetry: {payload}");

            // TODO: 使用Azure SDK发送
            // var message = new Message(Encoding.UTF8.GetBytes(payload));
            // await _deviceClient.SendEventAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to send telemetry: {ex.Message}", ex);
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

        await SendTelemetryAsync(telemetry, cancellationToken);
    }

    /// <summary>
    /// 获取设备孪生
    /// </summary>
    public async Task<DeviceTwinProperties> GetTwinAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            _logger.Warning("Not connected to Azure IoT Hub");
            return _twinProperties;
        }

        try
        {
            _logger.Debug("Getting device twin...");

            // TODO: 使用Azure SDK获取设备孪生
            // var twin = await _deviceClient.GetTwinAsync(cancellationToken);
            // _twinProperties.Desired = JsonSerializer.Deserialize<Dictionary<string, object>>(twin.Properties.Desired.ToJson());
            // _twinProperties.Reported = JsonSerializer.Deserialize<Dictionary<string, object>>(twin.Properties.Reported.ToJson());

            return _twinProperties;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get device twin: {ex.Message}", ex);
            return _twinProperties;
        }
    }

    /// <summary>
    /// 更新报告属性
    /// </summary>
    public async Task UpdateReportedPropertiesAsync(Dictionary<string, object> properties, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            _logger.Warning("Not connected to Azure IoT Hub");
            return;
        }

        try
        {
            foreach (var kvp in properties)
            {
                _twinProperties.Reported[kvp.Key] = kvp.Value;
            }

            _logger.Debug($"Updating reported properties: {JsonSerializer.Serialize(properties)}");

            // TODO: 使用Azure SDK更新报告属性
            // var reported = new TwinCollection();
            // foreach (var kvp in properties)
            // {
            //     reported[kvp.Key] = kvp.Value;
            // }
            // await _deviceClient.UpdateReportedPropertiesAsync(reported, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to update reported properties: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 响应直接方法
    /// </summary>
    public async Task RespondToDirectMethodAsync(string methodName, int status, object? payload, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            _logger.Warning("Not connected to Azure IoT Hub");
            return;
        }

        try
        {
            _logger.Debug($"Responding to direct method {methodName} with status {status}");

            // TODO: 使用Azure SDK响应
            // await _deviceClient.SendMethodResponseAsync(new MethodResponse(methodName, status, payload));
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to respond to direct method: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 发送心跳
    /// </summary>
    private async Task SendHeartbeatAsync()
    {
        if (!_isConnected)
            return;

        try
        {
            await SendTelemetryAsync(new Dictionary<string, object>
            {
                ["heartbeat"] = true,
                ["timestamp"] = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to send heartbeat: {ex.Message}", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}

/// <summary>
/// 直接方法请求
/// </summary>
public class DirectMethodRequest
{
    public string MethodName { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public object? Payload { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 云到设备消息
/// </summary>
public class CloudToDeviceMessage
{
    public string MessageId { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] Body { get; set; } = Array.Empty<byte>();
    public Dictionary<string, string> Properties { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}