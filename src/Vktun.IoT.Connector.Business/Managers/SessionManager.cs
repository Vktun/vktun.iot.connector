using System.Collections.Concurrent;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Business.Managers;

public class SessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<string, DeviceSession> _sessionsByDevice;
    private readonly ConcurrentDictionary<string, DeviceSession> _sessionsById;
    private readonly ILogger _logger;

    public int ActiveSessionCount => _sessionsByDevice.Count;
    public int TotalSessionCount => _sessionsById.Count;

    public event EventHandler<SessionCreatedEventArgs>? SessionCreated;
    public event EventHandler<SessionRemovedEventArgs>? SessionRemoved;

    public SessionManager(ILogger logger)
    {
        _sessionsByDevice = new ConcurrentDictionary<string, DeviceSession>();
        _sessionsById = new ConcurrentDictionary<string, DeviceSession>();
        _logger = logger;
    }

    public Task<DeviceSession> CreateSessionAsync(DeviceInfo device)
    {
        var session = new DeviceSession
        {
            SessionId = Guid.NewGuid().ToString(),
            DeviceId = device.DeviceId,
            DeviceInfo = device,
            Status = DeviceStatus.Online,
            CreateTime = DateTime.Now,
            LastActiveTime = DateTime.Now,
            CancellationTokenSource = new CancellationTokenSource()
        };

        _sessionsByDevice[device.DeviceId] = session;
        _sessionsById[session.SessionId] = session;

        SessionCreated?.Invoke(this, new SessionCreatedEventArgs
        {
            SessionId = session.SessionId,
            DeviceId = device.DeviceId,
            Timestamp = DateTime.Now
        });

        _logger.Info($"会话创建成功: {session.SessionId}, 设备: {device.DeviceId}");
        return Task.FromResult(session);
    }

    public Task<DeviceSession?> GetSessionAsync(string deviceId)
    {
        _sessionsByDevice.TryGetValue(deviceId, out var session);
        return Task.FromResult(session);
    }

    public Task<DeviceSession?> GetSessionByIdAsync(string sessionId)
    {
        _sessionsById.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    public Task<bool> RemoveSessionAsync(string deviceId)
    {
        if (!_sessionsByDevice.TryRemove(deviceId, out var session))
        {
            return Task.FromResult(false);
        }

        _sessionsById.TryRemove(session.SessionId, out _);
        session.CancellationTokenSource?.Cancel();
        session.CancellationTokenSource?.Dispose();

        SessionRemoved?.Invoke(this, new SessionRemovedEventArgs
        {
            SessionId = session.SessionId,
            DeviceId = deviceId,
            Reason = "主动断开",
            Timestamp = DateTime.Now
        });

        _logger.Info($"会话移除成功: {session.SessionId}, 设备: {deviceId}");
        return Task.FromResult(true);
    }

    public Task UpdateSessionActivityAsync(string deviceId)
    {
        if (_sessionsByDevice.TryGetValue(deviceId, out var session))
        {
            session.LastActiveTime = DateTime.Now;
            session.ReceiveCount++;
        }
        return Task.CompletedTask;
    }

    public Task<IEnumerable<DeviceSession>> GetAllSessionsAsync()
    {
        return Task.FromResult(_sessionsByDevice.Values.AsEnumerable());
    }

    public Task<IEnumerable<DeviceSession>> GetSessionsByStatusAsync(DeviceStatus status)
    {
        var sessions = _sessionsByDevice.Values.Where(s => s.Status == status);
        return Task.FromResult(sessions);
    }

    public async Task CleanupIdleSessionsAsync(TimeSpan idleTimeout)
    {
        var threshold = DateTime.Now - idleTimeout;
        var idleSessions = _sessionsByDevice.Values
            .Where(s => s.LastActiveTime < threshold)
            .ToList();

        foreach (var session in idleSessions)
        {
            await RemoveSessionAsync(session.DeviceId);
            _logger.Info($"清理空闲会话: {session.SessionId}, 设备: {session.DeviceId}");
        }
    }

    public async Task CleanupAllSessionsAsync()
    {
        var deviceIds = _sessionsByDevice.Keys.ToList();
        foreach (var deviceId in deviceIds)
        {
            await RemoveSessionAsync(deviceId);
        }
    }
}
