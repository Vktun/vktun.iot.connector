using System.Diagnostics;
using System.Runtime.InteropServices;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Concurrency.Monitors;

public class ResourceMonitor : IResourceMonitor
{
    private readonly IConfigurationProvider _configProvider;
    private readonly ILogger _logger;
    private Timer? _monitorTimer;
    private PerformanceCounter? _cpuCounter;
    private readonly object _lockObject = new();
    
    private double _cpuUsage;
    private long _memoryUsage;
    private int _activeConnections;
    private int _threadCount;
    private int _socketHandleCount;
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
                   _memoryUsage < config.Resource.MaxMemoryUsage;
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
        
        _logger.Info("资源监控器启动");
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
        
        _logger.Info("资源监控器停止");
    }

    public ResourceSnapshot GetSnapshot()
    {
        return new ResourceSnapshot
        {
            Timestamp = DateTime.Now,
            CpuUsage = _cpuUsage,
            MemoryUsage = _memoryUsage,
            AvailableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            ActiveConnections = _activeConnections,
            ThreadCount = _threadCount,
            SocketHandleCount = _socketHandleCount
        };
    }

    public Task<ResourceSnapshot> GetSnapshotAsync()
    {
        return Task.FromResult(GetSnapshot());
    }

    private void MonitorCallback(object? state)
    {
        try
        {
            UpdateMetrics();
            CheckThresholds();
            
            var snapshot = GetSnapshot();
            SnapshotTaken?.Invoke(this, new ResourceSnapshotEventArgs { Snapshot = snapshot });
        }
        catch (Exception ex)
        {
            _logger.Error($"资源监控异常: {ex.Message}", ex);
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

        var process = Process.GetCurrentProcess();
        memoryUsage = process.WorkingSet64;
        threadCount = process.Threads.Count;

        lock (_lockObject)
        {
            _cpuUsage = cpuUsage;
            _memoryUsage = memoryUsage;
            _threadCount = threadCount;
            _activeConnections = 0;
            _socketHandleCount = 0;
        }
    }

    private double GetLinuxCpuUsage()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return 0;
        }

        try
        {
            var process = Process.GetCurrentProcess();
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
                Message = $"CPU使用率超限: {_cpuUsage:F2}% > {config.Resource.MaxCpuUsage}%"
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
                Message = $"内存使用超限: {_memoryUsage / 1024 / 1024}MB > {config.Resource.MaxMemoryUsage / 1024 / 1024}MB"
            });
        }
    }

    public void UpdateConnectionCount(int count)
    {
        _activeConnections = count;
    }
}
