using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using PerformanceCounter = System.Diagnostics.PerformanceCounter;
using Process = System.Diagnostics.Process;

namespace Vktun.IoT.Connector.Concurrency.Monitors;

public class ResourceMonitor : IResourceMonitor
{
    private readonly IConfigurationProvider _configProvider;
    private readonly ILogger _logger;
    private readonly object _lockObject = new();
    private readonly ConcurrentDictionary<string, ICommunicationChannel> _trackedChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DeviceMetricContext> _deviceContexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ResourceDimensionMetrics> _deviceMetrics = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ResourceDimensionMetrics> _protocolMetrics = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<DiagnosticTrace> _diagnostics = new();

    private Timer? _monitorTimer;
    private PerformanceCounter? _cpuCounter;
    private DateTime _lastThroughputTimestamp = DateTime.UtcNow;
    private long _lastThroughputBytes;
    private double _cpuUsage;
    private long _memoryUsage;
    private int _activeConnections;
    private int _threadCount;
    private int _socketHandleCount;
    private double _throughput;
    private long _totalBytesSent;
    private long _totalBytesReceived;
    private bool _isRunning;

    public double CpuUsage => _cpuUsage;
    public long MemoryUsage => _memoryUsage;
    public int ActiveConnections => _activeConnections;
    public int ThreadCount => _threadCount;
    public int SocketHandleCount => _socketHandleCount;

    public bool IsHealthy
    {
        get
        {
            var config = _configProvider.GetConfig();
            return _cpuUsage < config.Resource.MaxCpuUsage &&
                   _memoryUsage < config.Resource.MaxMemoryUsage &&
                   _socketHandleCount < config.Resource.MaxSocketHandles;
        }
    }

    public event EventHandler<ResourceThresholdExceededEventArgs>? ThresholdExceeded;
    public event EventHandler<ResourceSnapshotEventArgs>? SnapshotTaken;

    public ResourceMonitor(IConfigurationProvider configProvider, ILogger logger)
    {
        _configProvider = configProvider;
        _logger = logger;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        }
    }

    public void Start()
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        var config = _configProvider.GetConfig();
        _monitorTimer = new Timer(MonitorCallback, null, 0, config.Resource.MonitorInterval);

        _logger.Info("Resource monitor started.");
    }

    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _monitorTimer?.Dispose();
        _monitorTimer = null;

        _logger.Info("Resource monitor stopped.");
    }

    public ResourceSnapshot GetSnapshot()
    {
        try
        {
            UpdateMetrics();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to refresh resource snapshot: {ex.Message}", ex);
        }

        return CreateSnapshot();
    }

    public Task<ResourceSnapshot> GetSnapshotAsync()
    {
        return Task.FromResult(GetSnapshot());
    }

    public void TrackChannel(ICommunicationChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var key = GetChannelKey(channel);
        if (!_trackedChannels.TryAdd(key, channel))
        {
            return;
        }

        channel.DeviceConnected += OnChannelDeviceConnected;
        channel.DeviceDisconnected += OnChannelDeviceDisconnected;
        channel.DataSent += OnChannelDataSent;
        channel.DataReceived += OnChannelDataReceived;
        channel.ErrorOccurred += OnChannelError;
    }

    public void UntrackChannel(string channelId)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return;
        }

        if (!_trackedChannels.TryRemove(channelId, out var channel))
        {
            return;
        }

        channel.DeviceConnected -= OnChannelDeviceConnected;
        channel.DeviceDisconnected -= OnChannelDeviceDisconnected;
        channel.DataSent -= OnChannelDataSent;
        channel.DataReceived -= OnChannelDataReceived;
        channel.ErrorOccurred -= OnChannelError;
    }

    public void RegisterDevice(DeviceInfo device, string channelId, string protocolId, ProtocolType protocolType)
    {
        ArgumentNullException.ThrowIfNull(device);

        var context = new DeviceMetricContext(
            device.DeviceId,
            channelId,
            string.IsNullOrWhiteSpace(protocolId) ? protocolType.ToString() : protocolId,
            protocolType);
        _deviceContexts[device.DeviceId] = context;

        lock (_lockObject)
        {
            var deviceMetric = GetOrCreateDeviceMetric(context);
            if (deviceMetric.ActiveConnections == 0)
            {
                deviceMetric.TotalConnections++;
            }

            deviceMetric.ActiveConnections = 1;

            var protocolMetric = GetOrCreateProtocolMetric(context);
            protocolMetric.TotalConnections++;
            protocolMetric.ActiveConnections = _deviceContexts.Values.Count(candidate =>
                GetProtocolKey(candidate.ProtocolId, candidate.ProtocolType)
                    .Equals(GetProtocolKey(context.ProtocolId, context.ProtocolType), StringComparison.OrdinalIgnoreCase));
        }
    }

    public void UnregisterDevice(string deviceId)
    {
        if (!_deviceContexts.TryRemove(deviceId, out var context))
        {
            return;
        }

        MarkDisconnected(context);
    }

    public void RecordOperation(ResourceOperationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var context = ResolveContext(record);
        var isSlow = record.ElapsedTime.TotalMilliseconds > _configProvider.GetConfig().Resource.SlowRequestThresholdMs;
        var failed = !record.Success || !record.ParseSucceeded || record.TimedOut || record.ExceptionOccurred;
        var latencyMs = Math.Max(0, (long)record.ElapsedTime.TotalMilliseconds);

        lock (_lockObject)
        {
            UpdateOperationMetrics(GetOrCreateDeviceMetric(context));
            UpdateOperationMetrics(GetOrCreateProtocolMetric(context));
        }

        RecordDiagnosticTrace(new DiagnosticTrace
        {
            Timestamp = record.Timestamp,
            DeviceId = record.DeviceId,
            ChannelId = context.ChannelId,
            ProtocolId = context.ProtocolId,
            ProtocolType = context.ProtocolType,
            TaskId = record.TaskId,
            ConfigVersion = record.ConfigVersion,
            Success = !failed,
            ElapsedTime = record.ElapsedTime,
            RequestBytes = record.RequestBytes,
            ResponseBytes = record.ResponseBytes,
            RequestFrameHex = ToHex(record.RequestFrame),
            ResponseFrameHex = ToHex(record.ResponseFrame),
            ParseError = record.ParseError,
            ErrorMessage = record.ErrorMessage
        });

        void UpdateOperationMetrics(ResourceDimensionMetrics metrics)
        {
            metrics.TotalRequests++;
            metrics.LastRequestTime = record.Timestamp;
            metrics.TotalLatencyMs += latencyMs;
            metrics.MaxLatencyMs = Math.Max(metrics.MaxLatencyMs, latencyMs);

            if (isSlow)
            {
                metrics.SlowRequests++;
            }

            if (failed)
            {
                metrics.FailedRequests++;
                metrics.LastErrorTime = record.Timestamp;
                metrics.LastErrorMessage = record.ParseError ?? record.ErrorMessage;
            }
            else
            {
                metrics.SuccessfulRequests++;
            }

            if (record.TimedOut)
            {
                metrics.TimeoutRequests++;
            }

            if (record.ExceptionOccurred)
            {
                metrics.ExceptionRequests++;
            }
        }
    }

    public void RecordReconnect(string deviceId, string channelId, string protocolId, ProtocolType protocolType, bool success)
    {
        var context = _deviceContexts.GetValueOrDefault(deviceId)
            ?? new DeviceMetricContext(deviceId, channelId, protocolId, protocolType);

        lock (_lockObject)
        {
            UpdateReconnectMetrics(GetOrCreateDeviceMetric(context));
            UpdateReconnectMetrics(GetOrCreateProtocolMetric(context));
        }

        void UpdateReconnectMetrics(ResourceDimensionMetrics metrics)
        {
            metrics.ReconnectAttempts++;
            if (success)
            {
                metrics.SuccessfulReconnects++;
            }
            else
            {
                metrics.FailedReconnects++;
                metrics.LastErrorTime = DateTime.Now;
                metrics.LastErrorMessage = "Reconnect failed.";
            }
        }
    }

    public void RecordDiagnosticTrace(DiagnosticTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);

        _diagnostics.Enqueue(trace);

        var maxTraces = Math.Max(1, _configProvider.GetConfig().Resource.MaxDiagnosticTraces);
        while (_diagnostics.Count > maxTraces && _diagnostics.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyDictionary<string, ResourceDimensionMetrics> GetDeviceMetrics()
    {
        lock (_lockObject)
        {
            return _deviceMetrics.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Snapshot(),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public IReadOnlyDictionary<string, ResourceDimensionMetrics> GetProtocolMetrics()
    {
        lock (_lockObject)
        {
            return _protocolMetrics.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Snapshot(),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public IReadOnlyList<DiagnosticTrace> GetRecentDiagnostics(int maxCount = 100)
    {
        return _diagnostics
            .Reverse()
            .Take(Math.Max(0, maxCount))
            .Reverse()
            .ToArray();
    }

    private void MonitorCallback(object? state)
    {
        try
        {
            UpdateMetrics();
            CheckThresholds();

            var snapshot = CreateSnapshot();
            SnapshotTaken?.Invoke(this, new ResourceSnapshotEventArgs { Snapshot = snapshot });
        }
        catch (Exception ex)
        {
            _logger.Error($"Resource monitor failed: {ex.Message}", ex);
        }
    }

    private void UpdateMetrics()
    {
        double cpuUsage;
        long memoryUsage;
        int threadCount;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _cpuCounter != null)
        {
            cpuUsage = _cpuCounter.NextValue();
        }
        else
        {
            cpuUsage = GetLinuxCpuUsage();
        }

        using var process = Process.GetCurrentProcess();
        memoryUsage = process.WorkingSet64;
        threadCount = process.Threads.Count;

        var channelSnapshots = _trackedChannels.Values
            .Select(channel => new
            {
                Channel = channel,
                Statistics = channel.Statistics
            })
            .ToArray();

        var activeConnections = channelSnapshots.Sum(item => item.Channel.ActiveConnections);
        var socketHandleCount = channelSnapshots.Sum(item => EstimateSocketHandles(item.Channel));
        var totalBytesSent = channelSnapshots.Sum(item => item.Statistics.TotalBytesSent);
        var totalBytesReceived = channelSnapshots.Sum(item => item.Statistics.TotalBytesReceived);

        if (totalBytesSent == 0 && totalBytesReceived == 0)
        {
            lock (_lockObject)
            {
                totalBytesSent = _deviceMetrics.Values.Sum(metric => metric.BytesSent);
                totalBytesReceived = _deviceMetrics.Values.Sum(metric => metric.BytesReceived);
            }
        }

        var now = DateTime.UtcNow;
        var totalBytes = totalBytesSent + totalBytesReceived;
        var elapsedSeconds = Math.Max(0.001, (now - _lastThroughputTimestamp).TotalSeconds);
        var throughput = Math.Max(0, (totalBytes - _lastThroughputBytes) / elapsedSeconds);

        lock (_lockObject)
        {
            _cpuUsage = cpuUsage;
            _memoryUsage = memoryUsage;
            _threadCount = threadCount;
            _activeConnections = activeConnections;
            _socketHandleCount = socketHandleCount;
            _totalBytesSent = totalBytesSent;
            _totalBytesReceived = totalBytesReceived;
            _throughput = throughput;
            _lastThroughputBytes = totalBytes;
            _lastThroughputTimestamp = now;
        }
    }

    private ResourceSnapshot CreateSnapshot()
    {
        return new ResourceSnapshot
        {
            Timestamp = DateTime.Now,
            CpuUsage = _cpuUsage,
            MemoryUsage = _memoryUsage,
            AvailableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            ActiveConnections = _activeConnections,
            ThreadCount = _threadCount,
            SocketHandleCount = _socketHandleCount,
            Throughput = _throughput,
            TotalBytesSent = _totalBytesSent,
            TotalBytesReceived = _totalBytesReceived,
            DeviceMetrics = GetDeviceMetrics(),
            ProtocolMetrics = GetProtocolMetrics(),
            RecentDiagnostics = GetRecentDiagnostics()
        };
    }

    private double GetLinuxCpuUsage()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return 0;
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            var startTime = DateTime.UtcNow;
            var startCpuUsage = process.TotalProcessorTime;

            Thread.Sleep(500);

            var endTime = DateTime.UtcNow;
            var endCpuUsage = process.TotalProcessorTime;

            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;
            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

            return cpuUsageTotal * 100;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get Linux CPU usage: {ex.Message}", ex);
            return 0;
        }
    }

    private void CheckThresholds()
    {
        var config = _configProvider.GetConfig();

        if (_cpuUsage > config.Resource.MaxCpuUsage)
        {
            ThresholdExceeded?.Invoke(this, new ResourceThresholdExceededEventArgs
            {
                ResourceType = "CPU",
                CurrentValue = _cpuUsage,
                ThresholdValue = config.Resource.MaxCpuUsage,
                Timestamp = DateTime.Now,
                Message = $"CPU usage exceeded: {_cpuUsage:F2}% > {config.Resource.MaxCpuUsage}%"
            });
        }

        if (_memoryUsage > config.Resource.MaxMemoryUsage)
        {
            ThresholdExceeded?.Invoke(this, new ResourceThresholdExceededEventArgs
            {
                ResourceType = "Memory",
                CurrentValue = _memoryUsage,
                ThresholdValue = config.Resource.MaxMemoryUsage,
                Timestamp = DateTime.Now,
                Message = $"Memory usage exceeded: {_memoryUsage / 1024 / 1024}MB > {config.Resource.MaxMemoryUsage / 1024 / 1024}MB"
            });
        }

        if (_socketHandleCount > config.Resource.MaxSocketHandles)
        {
            ThresholdExceeded?.Invoke(this, new ResourceThresholdExceededEventArgs
            {
                ResourceType = "SocketHandles",
                CurrentValue = _socketHandleCount,
                ThresholdValue = config.Resource.MaxSocketHandles,
                Timestamp = DateTime.Now,
                Message = $"Socket handle count exceeded: {_socketHandleCount} > {config.Resource.MaxSocketHandles}"
            });
        }
    }

    private void OnChannelDeviceConnected(object? sender, DeviceConnectedEventArgs e)
    {
        var channelId = sender is ICommunicationChannel channel ? channel.ChannelId : e.Device.ChannelId;
        RegisterDevice(e.Device, channelId, e.Device.ProtocolId, e.Device.ProtocolType);
    }

    private void OnChannelDeviceDisconnected(object? sender, DeviceDisconnectedEventArgs e)
    {
        if (_deviceContexts.TryRemove(e.DeviceId, out var context))
        {
            MarkDisconnected(context);
        }
    }

    private void OnChannelDataSent(object? sender, DataSentEventArgs e)
    {
        var context = ResolveContext(e.DeviceId, sender as ICommunicationChannel);
        AddBytes(context, sent: e.BytesSent, received: 0);
    }

    private void OnChannelDataReceived(object? sender, DataReceivedEventArgs e)
    {
        var context = ResolveContext(e.DeviceId, sender as ICommunicationChannel);
        AddBytes(context, sent: 0, received: e.Data.Length);
    }

    private void OnChannelError(object? sender, ChannelErrorEventArgs e)
    {
        var context = ResolveContext(e.DeviceId, sender as ICommunicationChannel);

        lock (_lockObject)
        {
            var deviceMetric = GetOrCreateDeviceMetric(context);
            deviceMetric.LastErrorTime = e.Timestamp;
            deviceMetric.LastErrorMessage = e.Message;

            var protocolMetric = GetOrCreateProtocolMetric(context);
            protocolMetric.LastErrorTime = e.Timestamp;
            protocolMetric.LastErrorMessage = e.Message;
        }
    }

    private void AddBytes(DeviceMetricContext context, int sent, int received)
    {
        lock (_lockObject)
        {
            var deviceMetric = GetOrCreateDeviceMetric(context);
            deviceMetric.BytesSent += sent;
            deviceMetric.BytesReceived += received;

            var protocolMetric = GetOrCreateProtocolMetric(context);
            protocolMetric.BytesSent += sent;
            protocolMetric.BytesReceived += received;
        }
    }

    private void MarkDisconnected(DeviceMetricContext context)
    {
        lock (_lockObject)
        {
            if (_deviceMetrics.TryGetValue(context.DeviceId, out var deviceMetric))
            {
                if (deviceMetric.ActiveConnections > 0)
                {
                    deviceMetric.TotalDisconnections++;
                }

                deviceMetric.ActiveConnections = 0;
            }

            var protocolKey = GetProtocolKey(context.ProtocolId, context.ProtocolType);
            if (_protocolMetrics.TryGetValue(protocolKey, out var protocolMetric))
            {
                if (protocolMetric.ActiveConnections > 0)
                {
                    protocolMetric.ActiveConnections--;
                    protocolMetric.TotalDisconnections++;
                }
            }
        }
    }

    private ResourceDimensionMetrics GetOrCreateDeviceMetric(DeviceMetricContext context)
    {
        if (_deviceMetrics.TryGetValue(context.DeviceId, out var metrics))
        {
            metrics.ChannelId = context.ChannelId;
            metrics.ProtocolId = context.ProtocolId;
            metrics.ProtocolType = context.ProtocolType;
            return metrics;
        }

        metrics = new ResourceDimensionMetrics
        {
            Key = context.DeviceId,
            DeviceId = context.DeviceId,
            ChannelId = context.ChannelId,
            ProtocolId = context.ProtocolId,
            ProtocolType = context.ProtocolType
        };
        _deviceMetrics[context.DeviceId] = metrics;
        return metrics;
    }

    private ResourceDimensionMetrics GetOrCreateProtocolMetric(DeviceMetricContext context)
    {
        var protocolKey = GetProtocolKey(context.ProtocolId, context.ProtocolType);
        if (_protocolMetrics.TryGetValue(protocolKey, out var metrics))
        {
            return metrics;
        }

        metrics = new ResourceDimensionMetrics
        {
            Key = protocolKey,
            ProtocolId = context.ProtocolId,
            ProtocolType = context.ProtocolType
        };
        _protocolMetrics[protocolKey] = metrics;
        return metrics;
    }

    private DeviceMetricContext ResolveContext(ResourceOperationRecord record)
    {
        var context = _deviceContexts.GetValueOrDefault(record.DeviceId);
        if (context != null)
        {
            return context;
        }

        return new DeviceMetricContext(
            record.DeviceId,
            record.ChannelId,
            string.IsNullOrWhiteSpace(record.ProtocolId) ? record.ProtocolType.ToString() : record.ProtocolId,
            record.ProtocolType);
    }

    private DeviceMetricContext ResolveContext(string deviceId, ICommunicationChannel? channel)
    {
        var context = _deviceContexts.GetValueOrDefault(deviceId);
        if (context != null)
        {
            return context;
        }

        return new DeviceMetricContext(
            deviceId,
            channel?.ChannelId ?? string.Empty,
            string.Empty,
            default);
    }

    private static string GetProtocolKey(string protocolId, ProtocolType protocolType)
    {
        return string.IsNullOrWhiteSpace(protocolId) ? protocolType.ToString() : protocolId;
    }

    private static string GetChannelKey(ICommunicationChannel channel)
    {
        return string.IsNullOrWhiteSpace(channel.ChannelId)
            ? $"{channel.CommunicationType}_{channel.GetHashCode():X}"
            : channel.ChannelId;
    }

    private static int EstimateSocketHandles(ICommunicationChannel channel)
    {
        if (!channel.IsConnected)
        {
            return 0;
        }

        return channel.CommunicationType switch
        {
            CommunicationType.Tcp => channel.ActiveConnections + (channel.ConnectionMode == ConnectionMode.Server ? 1 : 0),
            CommunicationType.Udp => 1,
            CommunicationType.Mqtt => 1,
            _ => 0
        };
    }

    private static string ToHex(byte[] data)
    {
        return data.Length == 0 ? string.Empty : Convert.ToHexString(data);
    }

    private sealed record DeviceMetricContext(
        string DeviceId,
        string ChannelId,
        string ProtocolId,
        ProtocolType ProtocolType);
}
