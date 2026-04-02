using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.Core.Models;

public class DeviceInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public CommunicationType CommunicationType { get; set; }
    public ConnectionMode ConnectionMode { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public string SerialPort { get; set; } = string.Empty;
    public int BaudRate { get; set; } = 9600;
    public int SlaveId { get; set; }
    public ProtocolType ProtocolType { get; set; }
    public string ProtocolId { get; set; } = string.Empty;
    public string ProtocolVersion { get; set; } = "1.0.0";
    public string ProtocolConfigPath { get; set; } = string.Empty;
    public DeviceStatus Status { get; set; } = DeviceStatus.Offline;
    public DateTime? LastConnectTime { get; set; }
    public DateTime? LastDataTime { get; set; }
    public int ReconnectCount { get; set; }
    public Dictionary<string, object> ExtendedProperties { get; set; } = new();
}

public class DeviceData
{
    public string DeviceId { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public ProtocolType ProtocolType { get; set; }
    public DateTime CollectTime { get; set; }
    public List<DataPoint> DataItems { get; set; } = new();
    public byte[]? RawData { get; set; }
    public bool IsValid { get; set; } = true;
    public string? ErrorMessage { get; set; }
}

public class DataPoint
{
    public string PointName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public object? Value { get; set; }
    public DataType DataType { get; set; }
    public string Unit { get; set; } = string.Empty;
    public double Quality { get; set; } = 100;
    public DateTime Timestamp { get; set; }
    public bool IsValid { get; set; } = true;
}

public class DeviceCommand
{
    public string CommandId { get; set; } = Guid.NewGuid().ToString();
    public string DeviceId { get; set; } = string.Empty;
    public string CommandName { get; set; } = string.Empty;
    public byte[]? Data { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public int Timeout { get; set; } = 5000;
    public DateTime CreateTime { get; set; } = DateTime.Now;
}

public class CommandResult
{
    public string CommandId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public byte[]? RequestData { get; set; }
    public byte[]? ResponseData { get; set; }
    public DeviceData? ParsedData { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan ElapsedTime { get; set; }
}

public class DeviceSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public string DeviceId { get; set; } = string.Empty;
    public DeviceInfo DeviceInfo { get; set; } = new();
    public DeviceStatus Status { get; set; } = DeviceStatus.Offline;
    public DateTime CreateTime { get; set; } = DateTime.Now;
    public DateTime LastActiveTime { get; set; } = DateTime.Now;
    public int ReceiveCount { get; set; }
    public int SendCount { get; set; }
    public long TotalBytesReceived { get; set; }
    public long TotalBytesSent { get; set; }
    public object? ConnectionHandle { get; set; }
    public CancellationTokenSource? CancellationTokenSource { get; set; }
}
