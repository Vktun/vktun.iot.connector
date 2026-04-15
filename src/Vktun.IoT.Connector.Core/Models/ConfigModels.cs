using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.Core.Models
{
    public class SdkConfig
    {
        public GlobalConfig Global { get; set; } = new GlobalConfig();
        public TcpConfig Tcp { get; set; } = new TcpConfig();
        public UdpConfig Udp { get; set; } = new UdpConfig();
        public HttpConfig Http { get; set; } = new HttpConfig();
        public SerialConfig Serial { get; set; } = new SerialConfig();
        public WirelessConfig Wireless { get; set; } = new WirelessConfig();
        public ThreadPoolConfig ThreadPool { get; set; } = new ThreadPoolConfig();
        public ResourceConfig Resource { get; set; } = new ResourceConfig();
        public CacheConfig Cache { get; set; } = new CacheConfig();
        public DataPersistenceConfig Persistence { get; set; } = new DataPersistenceConfig();
    }

    public class GlobalConfig
    {
        public int MaxConcurrentConnections { get; set; } = 1000;
        public int BufferSize { get; set; } = 8192;
        public int ConnectionTimeout { get; set; } = 5000;
        public int MaxReconnectCount { get; set; } = 100;
        public int ReconnectBaseInterval { get; set; } = 1000;
        public int ReconnectMaxInterval { get; set; } = 30000;
        public bool EnableDataCache { get; set; } = true;
        public int CacheMaxSize { get; set; } = 10000;
    }

    public class TcpConfig
    {
        public int MaxServerConnections { get; set; } = 1000;
        public int HeartbeatInterval { get; set; } = 15000;
        public int HeartbeatTimeout { get; set; } = 30000;
        public bool NoDelay { get; set; } = true;
        public int SessionIdleTimeout { get; set; } = 3600000;
        public int ListenBacklog { get; set; } = 100;
        public int ReceiveBufferSize { get; set; } = 8192;
        public int SendBufferSize { get; set; } = 8192;
    }

    public class UdpConfig
    {
        public int MaxOnlineDevices { get; set; } = 5000;
        public int HeartbeatCheckInterval { get; set; } = 20000;
        public int DeviceOfflineTimeout { get; set; } = 40000;
        public int ReceiveBufferSize { get; set; } = 65536;
        public int MaxDataRate { get; set; } = 1000;
    }

    public class HttpConfig
    {
        public string DefaultScheme { get; set; } = "http";
        public string DefaultMethod { get; set; } = "POST";
        public string DefaultContentType { get; set; } = "application/octet-stream";
        public int RequestTimeout { get; set; } = 5000;
        public int MaxConnectionsPerServer { get; set; } = 512;
        public int PooledConnectionLifetimeSeconds { get; set; } = 300;
    }

    public class SerialConfig
    {
        public int MaxDevicesPerPort { get; set; } = 32;
        public int PollingInterval { get; set; } = 100;
        public int ReceivePollingInterval { get; set; } = 10;
        public int ReadWriteTimeout { get; set; } = 500;
        public int MaxConcurrentPorts { get; set; } = 4;
    }

    public class WirelessConfig
    {
        public int HeartbeatInterval { get; set; } = 15000;
        public int OfflineTimeout { get; set; } = 40000;
        public int AtCommandTimeout { get; set; } = 2000;
        public int NbHeartbeatInterval { get; set; } = 30000;
        public int DataCacheSize { get; set; } = 2000;
        public int MaxDevicesPerChannel { get; set; } = 500;
    }

    public class ThreadPoolConfig
    {
        public int MinWorkerThreads { get; set; } = 10;
        public int MaxWorkerThreads { get; set; } = 100;
        public int MinCompletionPortThreads { get; set; } = 10;
        public int MaxCompletionPortThreads { get; set; } = 100;
        public int TaskQueueCapacity { get; set; } = 10000;
    }

    public class ResourceConfig
    {
        public double MaxCpuUsage { get; set; } = 80;
        public long MaxMemoryUsage { get; set; } = 1024 * 1024 * 1024;
        public int MaxSocketHandles { get; set; } = 10000;
        public int MonitorInterval { get; set; } = 5000;
        public bool EnableResourceMonitor { get; set; } = true;
        public int SlowRequestThresholdMs { get; set; } = 1000;
        public int MaxDiagnosticTraces { get; set; } = 256;
    }

    public class CacheConfig
    {
        public bool IsUseRedis { get; set; }
        public DataCacheBackend Backend { get; set; } = DataCacheBackend.Memory;
        public int MaxSize { get; set; }
        public RedisCacheConfig Redis { get; set; } = new RedisCacheConfig();
    }

    public class RedisCacheConfig
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string InstanceName { get; set; } = "vktun:iot:cache";
        public int Database { get; set; } = -1;
        public int ConnectTimeout { get; set; } = 5000;
        public int SyncTimeout { get; set; } = 5000;
        public bool AbortOnConnectFail { get; set; }
        public int KeyTtlSeconds { get; set; }
    }

    public class DataPersistenceConfig
    {
        public bool Enabled { get; set; }
        public DataPersistenceBackend Backend { get; set; } = DataPersistenceBackend.None;
        public string FilePath { get; set; } = "data/device-data.jsonl";
        public string SqliteConnectionString { get; set; } = "Data Source=vktun-device-data.db";
        public int MaxHistoryItems { get; set; } = 100000;
        public bool EnableWriteBuffer { get; set; } = true;
        public int WriteQueueCapacity { get; set; } = 10000;
        public DataPersistenceBackpressureStrategy BackpressureStrategy { get; set; } = DataPersistenceBackpressureStrategy.Wait;
        public DataPersistenceFailureStrategy FailureStrategy { get; set; } = DataPersistenceFailureStrategy.CacheOnly;
        public int ReplayBatchSize { get; set; } = 1000;
    }

    public class ProtocolTemplateExport
    {
        public DateTime ExportTime { get; set; }
        public string ExportVersion { get; set; } = "1.0";
        public ProtocolConfig Config { get; set; } = new ProtocolConfig();
    }

    public class ProtocolTemplateVersion
    {
        public string FilePath { get; set; } = string.Empty;
        public int ConfigVersion { get; set; }
        public string ProtocolVersion { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
        public long FileSize { get; set; }
    }

    public class ProtocolConfigValidationReport
    {
        public string ProtocolId { get; set; } = string.Empty;
        public string ProtocolName { get; set; } = string.Empty;
        public Vktun.IoT.Connector.Core.Enums.ProtocolType ProtocolType { get; set; }
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public DateTime ValidatedAt { get; set; }
    }
}
