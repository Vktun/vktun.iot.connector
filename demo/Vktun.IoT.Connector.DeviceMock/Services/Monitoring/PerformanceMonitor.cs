using System.Collections.Concurrent;
using Vktun.IoT.Connector.Core.Interfaces;

namespace Vktun.IoT.Connector.DeviceMock.Services.Monitoring;

public class PerformanceMonitor
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, PerformanceCounter> _counters = new();
    private readonly ConcurrentQueue<PerformanceSnapshot> _snapshots = new();
    private System.Threading.Timer? _monitoringTimer;
    private DateTime _startTime;
    
    public bool IsMonitoring { get; private set; }
    public TimeSpan MonitoringDuration => IsMonitoring ? DateTime.Now - _startTime : TimeSpan.Zero;
    
    public event EventHandler<PerformanceSnapshot>? PerformanceUpdated;
    
    public PerformanceMonitor(ILogger logger)
    {
        _logger = logger;
    }
    
    public void StartMonitoring(int intervalMs = 1000)
    {
        if (IsMonitoring)
        {
            return;
        }
        
        _startTime = DateTime.Now;
        IsMonitoring = true;
        
        _monitoringTimer = new Timer(_ =>
        {
            var snapshot = CaptureSnapshot();
            _snapshots.Enqueue(snapshot);
            
            while (_snapshots.Count > 3600)
            {
                _snapshots.TryDequeue(out var _);
            }
            
            PerformanceUpdated?.Invoke(this, snapshot);
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(intervalMs));
        
        _logger.Info("性能监控已启动");
    }
    
    public void StopMonitoring()
    {
        if (!IsMonitoring)
        {
            return;
        }
        
        _monitoringTimer?.Dispose();
        _monitoringTimer = null;
        IsMonitoring = false;
        
        _logger.Info("性能监控已停止");
    }
    
    public void IncrementCounter(string name)
    {
        var counter = _counters.GetOrAdd(name, _ => new PerformanceCounter());
        counter.Increment();
    }
    
    public void DecrementCounter(string name)
    {
        var counter = _counters.GetOrAdd(name, _ => new PerformanceCounter());
        counter.Decrement();
    }
    
    public void RecordValue(string name, double value)
    {
        var counter = _counters.GetOrAdd(name, _ => new PerformanceCounter());
        counter.RecordValue(value);
    }
    
    public void RecordDuration(string name, TimeSpan duration)
    {
        var counter = _counters.GetOrAdd(name, _ => new PerformanceCounter());
        counter.RecordDuration(duration);
    }
    
    public PerformanceCounter? GetCounter(string name)
    {
        return _counters.TryGetValue(name, out var counter) ? counter : null;
    }
    
    public Dictionary<string, PerformanceCounterStats> GetAllCounters()
    {
        return _counters.ToDictionary(c => c.Key, c => c.Value.GetStats());
    }
    
    public List<PerformanceSnapshot> GetSnapshots(DateTime? startTime = null, DateTime? endTime = null)
    {
        var snapshots = _snapshots.ToList();
        
        if (startTime.HasValue)
        {
            snapshots = snapshots.Where(s => s.Timestamp >= startTime.Value).ToList();
        }
        
        if (endTime.HasValue)
        {
            snapshots = snapshots.Where(s => s.Timestamp <= endTime.Value).ToList();
        }
        
        return snapshots;
    }
    
    public PerformanceReport GenerateReport()
    {
        var snapshots = _snapshots.ToList();
        if (snapshots.Count == 0)
        {
            return new PerformanceReport();
        }
        
        return new PerformanceReport
        {
            StartTime = _startTime,
            EndTime = DateTime.Now,
            Duration = MonitoringDuration,
            TotalSnapshots = snapshots.Count,
            Counters = GetAllCounters(),
            AverageCpuUsage = snapshots.Average(s => s.CpuUsage),
            AverageMemoryUsage = snapshots.Average(s => s.MemoryUsageMB),
            AverageResponseTime = snapshots.Average(s => s.AverageResponseTimeMs),
            TotalRequests = snapshots.Sum(s => s.RequestCount),
            TotalErrors = snapshots.Sum(s => s.ErrorCount),
            ErrorRate = snapshots.Sum(s => s.RequestCount) > 0 
                ? (double)snapshots.Sum(s => s.ErrorCount) / snapshots.Sum(s => s.RequestCount) * 100 
                : 0
        };
    }
    
    private PerformanceSnapshot CaptureSnapshot()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        
        return new PerformanceSnapshot
        {
            Timestamp = DateTime.Now,
            CpuUsage = GetCpuUsage(),
            MemoryUsageMB = process.WorkingSet64 / 1024.0 / 1024.0,
            ThreadCount = process.Threads.Count,
            HandleCount = process.HandleCount,
            RequestCount = GetCounter("Requests")?.Count ?? 0,
            ErrorCount = GetCounter("Errors")?.Count ?? 0,
            AverageResponseTimeMs = GetCounter("ResponseTime")?.AverageValue ?? 0,
            ConnectionCount = (int)(GetCounter("Connections")?.CurrentValue ?? 0),
            ThroughputPerSecond = CalculateThroughput()
        };
    }
    
    private double GetCpuUsage()
    {
        try
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var cpuUsage = process.TotalProcessorTime.TotalMilliseconds / 
                          (Environment.ProcessorCount * MonitoringDuration.TotalMilliseconds) * 100;
            return Math.Min(100, Math.Max(0, cpuUsage));
        }
        catch
        {
            return 0;
        }
    }
    
    private double CalculateThroughput()
    {
        var snapshots = _snapshots.ToList();
        if (snapshots.Count < 2)
        {
            return 0;
        }
        
        var recentSnapshots = snapshots.TakeLast(10).ToList();
        var totalRequests = recentSnapshots.Sum(s => s.RequestCount);
        var timeSpan = (recentSnapshots.Last().Timestamp - recentSnapshots.First().Timestamp).TotalSeconds;
        
        return timeSpan > 0 ? totalRequests / timeSpan : 0;
    }
}

public class PerformanceCounter
{
    private long _count;
    private double _totalValue;
    private double _minValue = double.MaxValue;
    private double _maxValue = double.MinValue;
    private TimeSpan _totalDuration;
    private readonly object _lock = new();
    
    public long Count => _count;
    public double CurrentValue { get; private set; }
    public double AverageValue => _count > 0 ? _totalValue / _count : 0;
    public double MinValue => _minValue;
    public double MaxValue => _maxValue;
    public TimeSpan AverageDuration => _count > 0 ? TimeSpan.FromTicks(_totalDuration.Ticks / _count) : TimeSpan.Zero;
    
    public void Increment()
    {
        Interlocked.Increment(ref _count);
    }
    
    public void Decrement()
    {
        Interlocked.Decrement(ref _count);
    }
    
    public void RecordValue(double value)
    {
        lock (_lock)
        {
            _count++;
            _totalValue += value;
            CurrentValue = value;
            
            if (value < _minValue)
            {
                _minValue = value;
            }
            
            if (value > _maxValue)
            {
                _maxValue = value;
            }
        }
    }
    
    public void RecordDuration(TimeSpan duration)
    {
        lock (_lock)
        {
            _totalDuration += duration;
        }
    }
    
    public PerformanceCounterStats GetStats()
    {
        return new PerformanceCounterStats
        {
            Count = _count,
            CurrentValue = CurrentValue,
            AverageValue = AverageValue,
            MinValue = _minValue == double.MaxValue ? 0 : _minValue,
            MaxValue = _maxValue == double.MinValue ? 0 : _maxValue,
            AverageDuration = AverageDuration
        };
    }
}

public class PerformanceSnapshot
{
    public DateTime Timestamp { get; set; }
    public double CpuUsage { get; set; }
    public double MemoryUsageMB { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public long RequestCount { get; set; }
    public long ErrorCount { get; set; }
    public double AverageResponseTimeMs { get; set; }
    public int ConnectionCount { get; set; }
    public double ThroughputPerSecond { get; set; }
}

public class PerformanceCounterStats
{
    public long Count { get; set; }
    public double CurrentValue { get; set; }
    public double AverageValue { get; set; }
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public TimeSpan AverageDuration { get; set; }
}

public class PerformanceReport
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public int TotalSnapshots { get; set; }
    public Dictionary<string, PerformanceCounterStats> Counters { get; set; } = new();
    public double AverageCpuUsage { get; set; }
    public double AverageMemoryUsage { get; set; }
    public double AverageResponseTime { get; set; }
    public long TotalRequests { get; set; }
    public long TotalErrors { get; set; }
    public double ErrorRate { get; set; }
}
