using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Core.Interfaces;

public interface ILogger
{
    void Log(LogLevel level, string message, Exception? exception = null);
    void Debug(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
    void Fatal(string message, Exception? exception = null);
}

public interface IConfigurationProvider
{
    SdkConfig GetConfig();
    Task<SdkConfig> LoadConfigAsync(string filePath);
    Task SaveConfigAsync(string filePath, SdkConfig config);
    Task<bool> UpdateConfigAsync(Action<SdkConfig> updateAction);
    event EventHandler<ConfigChangedEventArgs>? ConfigChanged;

    // Protocol template methods
    Task<List<ProtocolConfig>> LoadProtocolTemplatesAsync(string templatesDirectory);
    Task<ProtocolConfig?> LoadProtocolTemplateAsync(string filePath);
    Task<List<string>> GetProtocolTemplatePathsAsync(string templatesDirectory);
    Task SaveProtocolTemplateAsync(string filePath, ProtocolConfig config);
}

public class ConfigChangedEventArgs : EventArgs
{
    public SdkConfig OldConfig { get; set; } = new();
    public SdkConfig NewConfig { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public interface IHeartbeatManager
{
    int Interval { get; set; }
    int Timeout { get; set; }
    bool IsRunning { get; }
    
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    Task<bool> SendHeartbeatAsync(string deviceId);
    void RegisterDevice(string deviceId, int interval = 0);
    void UnregisterDevice(string deviceId);
    
    event EventHandler<HeartbeatMissedEventArgs>? HeartbeatMissed;
    event EventHandler<HeartbeatReceivedEventArgs>? HeartbeatReceived;
}

public class HeartbeatMissedEventArgs : EventArgs
{
    public string DeviceId { get; set; } = string.Empty;
    public int MissedCount { get; set; }
    public DateTime LastHeartbeatTime { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public class HeartbeatReceivedEventArgs : EventArgs
{
    public string DeviceId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
