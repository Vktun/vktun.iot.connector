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
}
