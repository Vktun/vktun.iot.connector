using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Core.Interfaces
{
    public interface IResourceMonitor
    {
        double CpuUsage { get; }
        long MemoryUsage { get; }
        int ActiveConnections { get; }
        int ThreadCount { get; }
        int SocketHandleCount { get; }
        bool IsHealthy { get; }
    
        void Start();
        void Stop();
        ResourceSnapshot GetSnapshot();
        Task<ResourceSnapshot> GetSnapshotAsync();
        void TrackChannel(ICommunicationChannel channel);
        void UntrackChannel(string channelId);
        void RegisterDevice(DeviceInfo device, string channelId, string protocolId, ProtocolType protocolType);
        void UnregisterDevice(string deviceId);
        void RecordOperation(ResourceOperationRecord record);
        void RecordReconnect(string deviceId, string channelId, string protocolId, ProtocolType protocolType, bool success);
        void RecordDiagnosticTrace(DiagnosticTrace trace);
        IReadOnlyDictionary<string, ResourceDimensionMetrics> GetDeviceMetrics();
        IReadOnlyDictionary<string, ResourceDimensionMetrics> GetProtocolMetrics();
        IReadOnlyList<DiagnosticTrace> GetRecentDiagnostics(int maxCount = 100);
    
        event EventHandler<ResourceThresholdExceededEventArgs>? ThresholdExceeded;
        event EventHandler<ResourceSnapshotEventArgs>? SnapshotTaken;
    }

    public class ResourceSnapshot
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public double CpuUsage { get; set; }
        public long MemoryUsage { get; set; }
        public long AvailableMemory { get; set; }
        public int ActiveConnections { get; set; }
        public int ThreadCount { get; set; }
        public int SocketHandleCount { get; set; }
        public int PendingTasks { get; set; }
        public double Throughput { get; set; }
        public long TotalBytesSent { get; set; }
        public long TotalBytesReceived { get; set; }
        public IReadOnlyDictionary<string, ResourceDimensionMetrics> DeviceMetrics { get; set; } =
            new Dictionary<string, ResourceDimensionMetrics>();
        public IReadOnlyDictionary<string, ResourceDimensionMetrics> ProtocolMetrics { get; set; } =
            new Dictionary<string, ResourceDimensionMetrics>();
        public IReadOnlyList<DiagnosticTrace> RecentDiagnostics { get; set; } = Array.Empty<DiagnosticTrace>();
    }

    public class ResourceThresholdExceededEventArgs : EventArgs
    {
        public string ResourceType { get; set; } = string.Empty;
        public double CurrentValue { get; set; }
        public double ThresholdValue { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Message { get; set; } = string.Empty;
    }

    public class ResourceSnapshotEventArgs : EventArgs
    {
        public ResourceSnapshot Snapshot { get; set; } = new ResourceSnapshot();
    }

    public class ResourceOperationRecord
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string DeviceId { get; set; } = string.Empty;
        public string ChannelId { get; set; } = string.Empty;
        public string ProtocolId { get; set; } = string.Empty;
        public ProtocolType ProtocolType { get; set; }
        public string TaskId { get; set; } = string.Empty;
        public TimeSpan ElapsedTime { get; set; }
        public bool Success { get; set; }
        public bool TimedOut { get; set; }
        public bool ExceptionOccurred { get; set; }
        public bool ParseSucceeded { get; set; } = true;
        public string? ErrorMessage { get; set; }
        public string? ParseError { get; set; }
        public int ConfigVersion { get; set; }
        public int RequestBytes { get; set; }
        public int ResponseBytes { get; set; }
        public byte[] RequestFrame { get; set; } = Array.Empty<byte>();
        public byte[] ResponseFrame { get; set; } = Array.Empty<byte>();
    }

    public class ResourceDimensionMetrics
    {
        public string Key { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string ChannelId { get; set; } = string.Empty;
        public string ProtocolId { get; set; } = string.Empty;
        public ProtocolType ProtocolType { get; set; }
        public int ActiveConnections { get; set; }
        public long TotalConnections { get; set; }
        public long TotalDisconnections { get; set; }
        public long TotalRequests { get; set; }
        public long SuccessfulRequests { get; set; }
        public long FailedRequests { get; set; }
        public long TimeoutRequests { get; set; }
        public long ExceptionRequests { get; set; }
        public long SlowRequests { get; set; }
        public long ReconnectAttempts { get; set; }
        public long SuccessfulReconnects { get; set; }
        public long FailedReconnects { get; set; }
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }
        public long TotalLatencyMs { get; set; }
        public long MaxLatencyMs { get; set; }
        public DateTime? LastRequestTime { get; set; }
        public DateTime? LastErrorTime { get; set; }
        public string? LastErrorMessage { get; set; }

        public double AverageLatencyMs => TotalRequests > 0 ? (double)TotalLatencyMs / TotalRequests : 0;
        public double TimeoutRate => TotalRequests > 0 ? (double)TimeoutRequests / TotalRequests : 0;
        public double ExceptionRate => TotalRequests > 0 ? (double)ExceptionRequests / TotalRequests : 0;
        public double SlowRequestRate => TotalRequests > 0 ? (double)SlowRequests / TotalRequests : 0;
        public double ReconnectRate => TotalConnections > 0 ? (double)ReconnectAttempts / TotalConnections : 0;

        public ResourceDimensionMetrics Snapshot()
        {
            return new ResourceDimensionMetrics
            {
                Key = Key,
                DeviceId = DeviceId,
                ChannelId = ChannelId,
                ProtocolId = ProtocolId,
                ProtocolType = ProtocolType,
                ActiveConnections = ActiveConnections,
                TotalConnections = TotalConnections,
                TotalDisconnections = TotalDisconnections,
                TotalRequests = TotalRequests,
                SuccessfulRequests = SuccessfulRequests,
                FailedRequests = FailedRequests,
                TimeoutRequests = TimeoutRequests,
                ExceptionRequests = ExceptionRequests,
                SlowRequests = SlowRequests,
                ReconnectAttempts = ReconnectAttempts,
                SuccessfulReconnects = SuccessfulReconnects,
                FailedReconnects = FailedReconnects,
                BytesSent = BytesSent,
                BytesReceived = BytesReceived,
                TotalLatencyMs = TotalLatencyMs,
                MaxLatencyMs = MaxLatencyMs,
                LastRequestTime = LastRequestTime,
                LastErrorTime = LastErrorTime,
                LastErrorMessage = LastErrorMessage
            };
        }
    }

    public class DiagnosticTrace
    {
        public string TraceId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string DeviceId { get; set; } = string.Empty;
        public string ChannelId { get; set; } = string.Empty;
        public string ProtocolId { get; set; } = string.Empty;
        public ProtocolType ProtocolType { get; set; }
        public string TaskId { get; set; } = string.Empty;
        public int ConfigVersion { get; set; }
        public bool Success { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public int RequestBytes { get; set; }
        public int ResponseBytes { get; set; }
        public string RequestFrameHex { get; set; } = string.Empty;
        public string ResponseFrameHex { get; set; } = string.Empty;
        public string? ParseError { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
