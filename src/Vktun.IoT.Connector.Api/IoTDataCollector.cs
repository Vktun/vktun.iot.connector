using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Api;

public class IoTDataCollector : IIoTDataCollector
{
    private readonly IDeviceManager _deviceManager;
    private readonly ISessionManager _sessionManager;
    private readonly ITaskScheduler _taskScheduler;
    private readonly IResourceMonitor _resourceMonitor;
    private readonly IConfigurationProvider _configProvider;
    private readonly IDataProvider _dataProvider;
    private readonly IHeartbeatManager _heartbeatManager;
    private readonly ILogger _logger;
    private readonly Dictionary<string, ICommunicationChannel> _channels;
    
    private bool _isRunning;
    private bool _isDisposed;
    private CancellationTokenSource? _cancellationTokenSource;

    public bool IsRunning => _isRunning;
    public int ConnectedDeviceCount => _sessionManager.ActiveSessionCount;

    public event EventHandler<DeviceStatusChangedEventArgs>? DeviceStatusChanged;
    public event EventHandler<DataReceivedEventArgs>? DataReceived;
    public event EventHandler<DeviceErrorEventArgs>? DeviceError;
    public event EventHandler<ResourceThresholdExceededEventArgs>? ResourceThresholdExceeded;

    public IoTDataCollector(
        IDeviceManager deviceManager,
        ISessionManager sessionManager,
        ITaskScheduler taskScheduler,
        IResourceMonitor resourceMonitor,
        IConfigurationProvider configProvider,
        IDataProvider dataProvider,
        IHeartbeatManager heartbeatManager,
        ILogger logger)
    {
        _deviceManager = deviceManager;
        _sessionManager = sessionManager;
        _taskScheduler = taskScheduler;
        _resourceMonitor = resourceMonitor;
        _configProvider = configProvider;
        _dataProvider = dataProvider;
        _heartbeatManager = heartbeatManager;
        _logger = logger;
        _channels = new Dictionary<string, ICommunicationChannel>();
        
        SubscribeEvents();
    }

    private void SubscribeEvents()
    {
        _deviceManager.DeviceStatusChanged += OnDeviceStatusChanged;
        _resourceMonitor.ThresholdExceeded += OnResourceThresholdExceeded;
        _heartbeatManager.HeartbeatMissed += OnHeartbeatMissed;
        _taskScheduler.TaskCompleted += OnTaskCompleted;
        _taskScheduler.TaskFailed += OnTaskFailed;
    }

    public async Task InitializeAsync(SdkConfig? config = null)
    {
        if (config != null)
        {
            await _configProvider.UpdateConfigAsync(c =>
            {
                c.Global = config.Global;
                c.Tcp = config.Tcp;
                c.Udp = config.Udp;
                c.Serial = config.Serial;
                c.Wireless = config.Wireless;
                c.ThreadPool = config.ThreadPool;
                c.Resource = config.Resource;
            });
        }
        
        var sdkConfig = _configProvider.GetConfig();
        _logger.Info($"SDK初始化完成，最大并发连接数: {sdkConfig.Global.MaxConcurrentConnections}");
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            _logger.Warning("SDK已在运行中");
            return;
        }

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        _resourceMonitor.Start();
        await _taskScheduler.StartAsync(_cancellationTokenSource.Token);
        await _heartbeatManager.StartAsync(_cancellationTokenSource.Token);
        
        _isRunning = true;
        _logger.Info("SDK启动成功");
    }

    public async Task StopAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _cancellationTokenSource?.Cancel();
        
        await _heartbeatManager.StopAsync();
        await _taskScheduler.StopAsync();
        _resourceMonitor.Stop();
        
        await _deviceManager.DisconnectAllAsync();
        await _sessionManager.CleanupAllSessionsAsync();
        
        foreach (var channel in _channels.Values)
        {
            await channel.CloseAsync();
        }
        _channels.Clear();
        
        _logger.Info("SDK已停止");
    }

    public async Task<bool> AddDeviceAsync(DeviceInfo device)
    {
        var result = await _deviceManager.AddDeviceAsync(device);
        if (result)
        {
            _heartbeatManager.RegisterDevice(device.DeviceId);
            _logger.Info($"设备添加成功: {device.DeviceId}");
        }
        return result;
    }

    public async Task<bool> RemoveDeviceAsync(string deviceId)
    {
        _heartbeatManager.UnregisterDevice(deviceId);
        var result = await _deviceManager.RemoveDeviceAsync(deviceId);
        if (result)
        {
            _logger.Info($"设备移除成功: {deviceId}");
        }
        return result;
    }

    public Task<DeviceInfo?> GetDeviceAsync(string deviceId)
    {
        return _deviceManager.GetDeviceAsync(deviceId);
    }

    public Task<IEnumerable<DeviceInfo>> GetAllDevicesAsync()
    {
        return _deviceManager.GetAllDevicesAsync();
    }

    public async Task<bool> ConnectDeviceAsync(string deviceId)
    {
        var result = await _deviceManager.ConnectDeviceAsync(deviceId);
        if (result)
        {
            _logger.Info($"设备连接成功: {deviceId}");
        }
        return result;
    }

    public async Task<bool> DisconnectDeviceAsync(string deviceId)
    {
        var result = await _deviceManager.DisconnectDeviceAsync(deviceId);
        if (result)
        {
            _logger.Info($"设备断开成功: {deviceId}");
        }
        return result;
    }

    public async Task<DeviceData?> CollectDataAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var device = await _deviceManager.GetDeviceAsync(deviceId);
        if (device == null)
        {
            _logger.Warning($"设备不存在: {deviceId}");
            return null;
        }

        var command = new DeviceCommand
        {
            DeviceId = deviceId,
            CommandName = "CollectData",
            Priority = TaskPriority.Normal
        };

        var result = await _taskScheduler.SubmitTaskAsync(command, cancellationToken);
        var taskResult = await _taskScheduler.GetTaskResultAsync(result, cancellationToken);
        
        if (taskResult.Success && taskResult.ResponseData != null)
        {
            var data = new DeviceData
            {
                DeviceId = deviceId,
                CollectTime = DateTime.Now,
                RawData = taskResult.ResponseData
            };
            
            await _dataProvider.WriteDataAsync(data);
            return data;
        }
        
        return null;
    }

    public async Task<CommandResult> SendCommandAsync(DeviceCommand command, CancellationToken cancellationToken = default)
    {
        var taskId = await _taskScheduler.SubmitTaskAsync(command, cancellationToken);
        var result = await _taskScheduler.GetTaskResultAsync(taskId, cancellationToken);
        
        return new CommandResult
        {
            CommandId = result.CommandId,
            Success = result.Success,
            ResponseData = result.ResponseData,
            ErrorMessage = result.ErrorMessage,
            ElapsedTime = result.ElapsedTime
        };
    }

    public Task<SdkConfig> GetConfigAsync()
    {
        return Task.FromResult(_configProvider.GetConfig());
    }

    public async Task UpdateConfigAsync(SdkConfig config)
    {
        await _configProvider.UpdateConfigAsync(c =>
        {
            c.Global = config.Global;
            c.Tcp = config.Tcp;
            c.Udp = config.Udp;
            c.Serial = config.Serial;
            c.Wireless = config.Wireless;
            c.ThreadPool = config.ThreadPool;
            c.Resource = config.Resource;
        });
    }

    private void OnDeviceStatusChanged(object? sender, DeviceStatusChangedEventArgs e)
    {
        DeviceStatusChanged?.Invoke(this, e);
    }

    private void OnResourceThresholdExceeded(object? sender, ResourceThresholdExceededEventArgs e)
    {
        _logger.Warning($"资源阈值超限: {e.ResourceType}, 当前值: {e.CurrentValue}, 阈值: {e.ThresholdValue}");
        ResourceThresholdExceeded?.Invoke(this, e);
    }

    private void OnHeartbeatMissed(object? sender, HeartbeatMissedEventArgs e)
    {
        _logger.Warning($"设备心跳丢失: {e.DeviceId}, 丢失次数: {e.MissedCount}");
        
        DeviceError?.Invoke(this, new DeviceErrorEventArgs
        {
            DeviceId = e.DeviceId,
            ErrorMessage = $"心跳丢失 {e.MissedCount} 次",
            Timestamp = DateTime.Now
        });
    }

    private void OnTaskCompleted(object? sender, TaskCompletedEventArgs e)
    {
        _logger.Debug($"任务完成: {e.TaskId}, 设备: {e.DeviceId}");
    }

    private void OnTaskFailed(object? sender, TaskFailedEventArgs e)
    {
        _logger.Error($"任务失败: {e.TaskId}, 设备: {e.DeviceId}, 错误: {e.ErrorMessage}");
        
        DeviceError?.Invoke(this, new DeviceErrorEventArgs
        {
            DeviceId = e.DeviceId,
            ErrorMessage = e.ErrorMessage,
            Exception = e.Exception,
            Timestamp = DateTime.Now
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await StopAsync();
        _cancellationTokenSource?.Dispose();
        _isDisposed = true;
        
        GC.SuppressFinalize(this);
    }
}
