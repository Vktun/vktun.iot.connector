using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector;

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

    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isRunning;
    private bool _isDisposed;

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
        SubscribeEvents();
    }

    public bool IsRunning => _isRunning;
    public int ConnectedDeviceCount => _sessionManager.ActiveSessionCount;

    public event EventHandler<DeviceStatusChangedEventArgs>? DeviceStatusChanged;
    public event EventHandler<DataReceivedEventArgs>? DataReceived;
    public event EventHandler<DeviceErrorEventArgs>? DeviceError;
    public event EventHandler<ResourceThresholdExceededEventArgs>? ResourceThresholdExceeded;

    public async Task InitializeAsync(SdkConfig? config = null)
    {
        if (config != null)
        {
            await _configProvider.UpdateConfigAsync(existing =>
            {
                existing.Global = config.Global;
                existing.Tcp = config.Tcp;
                existing.Udp = config.Udp;
                existing.Http = config.Http;
                existing.Serial = config.Serial;
                existing.Wireless = config.Wireless;
                existing.ThreadPool = config.ThreadPool;
                existing.Resource = config.Resource;
                existing.Cache = config.Cache;
                existing.Persistence = config.Persistence;
            }).ConfigureAwait(false);
        }

        _logger.Info($"SDK initialized. Max connections: {_configProvider.GetConfig().Global.MaxConcurrentConnections}");
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return;
        }

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _resourceMonitor.Start();
        await _taskScheduler.StartAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
        await _heartbeatManager.StartAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
        _isRunning = true;
        _logger.Info("SDK started.");
    }

    public async Task StopAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _cancellationTokenSource?.Cancel();
        await _heartbeatManager.StopAsync().ConfigureAwait(false);
        await _taskScheduler.StopAsync().ConfigureAwait(false);
        _resourceMonitor.Stop();
        await _deviceManager.DisconnectAllAsync().ConfigureAwait(false);
        await _sessionManager.CleanupAllSessionsAsync().ConfigureAwait(false);
        _logger.Info("SDK stopped.");
    }

    public async Task<bool> AddDeviceAsync(DeviceInfo device)
    {
        var added = await _deviceManager.AddDeviceAsync(device).ConfigureAwait(false);
        if (added)
        {
            _heartbeatManager.RegisterDevice(device.DeviceId);
        }

        return added;
    }

    public async Task<bool> RemoveDeviceAsync(string deviceId)
    {
        _heartbeatManager.UnregisterDevice(deviceId);
        return await _deviceManager.RemoveDeviceAsync(deviceId).ConfigureAwait(false);
    }

    public Task<DeviceInfo?> GetDeviceAsync(string deviceId)
    {
        return _deviceManager.GetDeviceAsync(deviceId);
    }

    public Task<IEnumerable<DeviceInfo>> GetAllDevicesAsync()
    {
        return _deviceManager.GetAllDevicesAsync();
    }

    public Task<bool> ConnectDeviceAsync(string deviceId)
    {
        return _deviceManager.ConnectDeviceAsync(deviceId);
    }

    public Task<bool> DisconnectDeviceAsync(string deviceId)
    {
        return _deviceManager.DisconnectDeviceAsync(deviceId);
    }

    public async Task<DeviceData?> CollectDataAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var device = await _deviceManager.GetDeviceAsync(deviceId).ConfigureAwait(false);
        if (device == null)
        {
            return null;
        }

        var command = new DeviceCommand
        {
            DeviceId = deviceId,
            CommandName = "CollectData",
            Priority = TaskPriority.Normal,
            Timeout = device.ProtocolType == ProtocolType.ModbusRtu ? 2000 : 5000
        };

        var taskId = await _taskScheduler.SubmitTaskAsync(command, cancellationToken).ConfigureAwait(false);
        var result = await _taskScheduler.GetTaskResultAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return null;
        }

        var data = result.ParsedData ?? new DeviceData
        {
            DeviceId = deviceId,
            ChannelId = device.ChannelId,
            ProtocolType = device.ProtocolType,
            CollectTime = DateTime.Now,
            RawData = result.ResponseData
        };

        await _dataProvider.WriteDataAsync(data).ConfigureAwait(false);
        return data;
    }

    public async Task<CommandResult> SendCommandAsync(DeviceCommand command, CancellationToken cancellationToken = default)
    {
        var taskId = await _taskScheduler.SubmitTaskAsync(command, cancellationToken).ConfigureAwait(false);
        var result = await _taskScheduler.GetTaskResultAsync(taskId, cancellationToken).ConfigureAwait(false);
        return new CommandResult
        {
            CommandId = result.CommandId,
            Success = result.Success,
            ResponseData = result.ResponseData,
            ErrorMessage = result.ErrorMessage,
            ElapsedTime = result.ElapsedTime
        };
    }

    public Task<ResourceSnapshot> GetResourceSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _resourceMonitor.GetSnapshotAsync();
    }

    public Task<SdkConfig> GetConfigAsync()
    {
        return Task.FromResult(_configProvider.GetConfig());
    }

    public Task UpdateConfigAsync(SdkConfig config)
    {
        return _configProvider.UpdateConfigAsync(existing =>
        {
            existing.Global = config.Global;
            existing.Tcp = config.Tcp;
            existing.Udp = config.Udp;
            existing.Http = config.Http;
            existing.Serial = config.Serial;
            existing.Wireless = config.Wireless;
            existing.ThreadPool = config.ThreadPool;
            existing.Resource = config.Resource;
            existing.Cache = config.Cache;
            existing.Persistence = config.Persistence;
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _cancellationTokenSource?.Dispose();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    private void SubscribeEvents()
    {
        _deviceManager.DeviceStatusChanged += (_, args) => DeviceStatusChanged?.Invoke(this, args);
        _resourceMonitor.ThresholdExceeded += (_, args) => ResourceThresholdExceeded?.Invoke(this, args);
        _heartbeatManager.HeartbeatMissed += OnHeartbeatMissed;
        _taskScheduler.TaskFailed += OnTaskFailed;
        _dataProvider.DataWritten += OnDataWritten;
    }

    private void OnHeartbeatMissed(object? sender, HeartbeatMissedEventArgs e)
    {
        DeviceError?.Invoke(this, new DeviceErrorEventArgs
        {
            DeviceId = e.DeviceId,
            ErrorMessage = $"Heartbeat missed {e.MissedCount} times.",
            Timestamp = DateTime.Now
        });
    }

    private void OnTaskFailed(object? sender, TaskFailedEventArgs e)
    {
        DeviceError?.Invoke(this, new DeviceErrorEventArgs
        {
            DeviceId = e.DeviceId,
            ErrorMessage = e.ErrorMessage,
            Exception = e.Exception,
            Timestamp = DateTime.Now
        });
    }

    private void OnDataWritten(object? sender, DataWrittenEventArgs e)
    {
        DataReceived?.Invoke(this, new DataReceivedEventArgs
        {
            DeviceId = e.DeviceId,
            Data = e.Data.RawData ?? Array.Empty<byte>(),
            Timestamp = e.Timestamp
        });
    }
}
