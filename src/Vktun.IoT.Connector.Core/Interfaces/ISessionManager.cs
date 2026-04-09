using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Core.Interfaces
{
    public interface ISessionManager
    {
        int ActiveSessionCount { get; }
        int TotalSessionCount { get; }
    
        Task<DeviceSession> CreateSessionAsync(DeviceInfo device);
        Task<DeviceSession?> GetSessionAsync(string deviceId);
        Task<DeviceSession?> GetSessionByIdAsync(string sessionId);
        Task<bool> RemoveSessionAsync(string deviceId);
        Task UpdateSessionActivityAsync(string deviceId);
        Task<IEnumerable<DeviceSession>> GetAllSessionsAsync();
        Task<IEnumerable<DeviceSession>> GetSessionsByStatusAsync(DeviceStatus status);
        Task CleanupIdleSessionsAsync(TimeSpan idleTimeout);
        Task CleanupAllSessionsAsync();
    
        event EventHandler<SessionCreatedEventArgs>? SessionCreated;
        event EventHandler<SessionRemovedEventArgs>? SessionRemoved;
    }

    public class SessionCreatedEventArgs : EventArgs
    {
        public string SessionId { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class SessionRemovedEventArgs : EventArgs
    {
        public string SessionId { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
