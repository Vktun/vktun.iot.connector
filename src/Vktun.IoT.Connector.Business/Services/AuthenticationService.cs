using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Business.Services;

/// <summary>
/// 认证服务实现
/// </summary>
public class AuthenticationService : IAuthenticationProvider, IDisposable
{
    private readonly AuthenticationConfig _config;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, ApiKeyCredential> _apiKeys;
    private readonly ConcurrentDictionary<string, ConnectionInfo> _activeConnections;
    private readonly ConcurrentDictionary<string, List<DateTime>> _connectionRateLog;
    private readonly Timer _cleanupTimer;
    private readonly object _rateLimitLock = new();

    public AuthenticationService(AuthenticationConfig config, ILogger logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _apiKeys = new ConcurrentDictionary<string, ApiKeyCredential>();
        _activeConnections = new ConcurrentDictionary<string, ConnectionInfo>();
        _connectionRateLog = new ConcurrentDictionary<string, List<DateTime>>();

        // 初始化API Keys
        foreach (var apiKey in _config.ApiKeys)
        {
            if (apiKey.IsActive && (!apiKey.ExpiryDate.HasValue || apiKey.ExpiryDate.Value > DateTime.UtcNow))
            {
                _apiKeys.TryAdd(apiKey.Key, apiKey);
            }
        }

        // 定期清理过期的速率限制记录
        _cleanupTimer = new Timer(CleanupExpiredRecords, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// 当前活跃连接数
    /// </summary>
    public int ActiveConnectionCount => _activeConnections.Count;

    /// <summary>
    /// 验证连接请求
    /// </summary>
    public async Task<AuthenticationResult> AuthenticateAsync(
        IPEndPoint remoteEndPoint,
        Dictionary<string, string>? credentials = null)
    {
        // 1. 检查IP白名单/黑名单
        if (!IsIpAllowed(remoteEndPoint.Address))
        {
            _logger.Warning($"Connection rejected: IP {remoteEndPoint.Address} is not allowed");
            return AuthenticationResult.CreateFailure("IP address not allowed");
        }

        // 2. 检查连接速率限制
        if (!CheckConnectionRateLimit(remoteEndPoint))
        {
            _logger.Warning($"Connection rate limit exceeded from {remoteEndPoint.Address}");
            return AuthenticationResult.CreateFailure("Connection rate limit exceeded");
        }

        // 3. 检查最大并发连接数
        if (_config.MaxConcurrentConnections > 0 && _activeConnections.Count >= _config.MaxConcurrentConnections)
        {
            _logger.Warning($"Maximum concurrent connections reached: {_config.MaxConcurrentConnections}");
            return AuthenticationResult.CreateFailure("Maximum connections reached");
        }

        // 4. 如果未启用认证，直接通过
        if (!_config.Enabled || _config.Method == AuthMethod.None)
        {
            _logger.Debug($"Connection accepted without authentication from {remoteEndPoint.Address}");
            return AuthenticationResult.CreateSuccess("anonymous");
        }

        // 5. 根据认证方式进行验证
        return _config.Method switch
        {
            AuthMethod.ApiKey => AuthenticateByApiKey(credentials),
            AuthMethod.Certificate => AuthenticateByCertificate(remoteEndPoint),
            AuthMethod.Token => await AuthenticateByTokenAsync(credentials),
            _ => AuthenticationResult.CreateFailure("Unsupported authentication method")
        };
    }

    /// <summary>
    /// API Key 认证
    /// </summary>
    private AuthenticationResult AuthenticateByApiKey(Dictionary<string, string>? credentials)
    {
        if (credentials == null || !credentials.TryGetValue("ApiKey", out var apiKey) || string.IsNullOrEmpty(apiKey))
        {
            return AuthenticationResult.CreateFailure("API Key is required");
        }

        if (!_apiKeys.TryGetValue(apiKey, out var credential))
        {
            _logger.Warning($"Invalid API Key used");
            return AuthenticationResult.CreateFailure("Invalid API Key");
        }

        // 检查是否过期
        if (credential.ExpiryDate.HasValue && credential.ExpiryDate.Value < DateTime.UtcNow)
        {
            _logger.Warning($"API Key expired: {credential.Description}");
            return AuthenticationResult.CreateFailure("API Key expired");
        }

        // 检查是否激活
        if (!credential.IsActive)
        {
            return AuthenticationResult.CreateFailure("API Key is disabled");
        }

        _logger.Info($"Authentication successful with API Key: {credential.Description}");
        return AuthenticationResult.CreateSuccess(
            credential.AllowedDevices.FirstOrDefault() ?? "authenticated",
            GenerateSessionToken());
    }

    /// <summary>
    /// 证书认证（占位实现，实际在TLS握手时完成）
    /// </summary>
    private AuthenticationResult AuthenticateByCertificate(IPEndPoint remoteEndPoint)
    {
        // 证书验证在TLS握手阶段完成，这里只记录日志
        _logger.Info($"Client connected with certificate from {remoteEndPoint.Address}");
        return AuthenticationResult.CreateSuccess($"cert_{remoteEndPoint.Address}");
    }

    /// <summary>
    /// Token认证（预留实现）
    /// </summary>
    private Task<AuthenticationResult> AuthenticateByTokenAsync(Dictionary<string, string>? credentials)
    {
        throw new NotImplementedException("Token authentication not implemented");
    }

    /// <summary>
    /// 验证设备权限
    /// </summary>
    public Task<bool> AuthorizeAsync(string deviceId, string resource)
    {
        // 简单实现：检查API Key是否允许访问该设备
        foreach (var kvp in _apiKeys)
        {
            var credential = kvp.Value;
            if (credential.AllowedDevices.Count == 0 || credential.AllowedDevices.Contains(deviceId))
            {
                return Task.FromResult(true);
            }
        }

        _logger.Warning($"Device {deviceId} is not authorized to access {resource}");
        return Task.FromResult(false);
    }

    /// <summary>
    /// 检查IP是否允许
    /// </summary>
    public bool IsIpAllowed(IPAddress ipAddress)
    {
        // 检查黑名单
        if (_config.IpBlacklist.Any(ip => IPAddress.TryParse(ip, out var blockedIp) && blockedIp.Equals(ipAddress)))
        {
            return false;
        }

        // 如果有白名单，必须在白名单中
        if (_config.IpWhitelist.Count > 0)
        {
            return _config.IpWhitelist.Any(ip => IPAddress.TryParse(ip, out var allowedIp) && allowedIp.Equals(ipAddress));
        }

        return true;
    }

    /// <summary>
    /// 检查连接速率限制
    /// </summary>
    public bool CheckConnectionRateLimit(IPEndPoint remoteEndPoint)
    {
        if (_config.ConnectionRateLimit <= 0)
            return true;

        var ipKey = remoteEndPoint.Address.ToString();
        var now = DateTime.UtcNow;
        var windowStart = now.AddSeconds(-1);

        lock (_rateLimitLock)
        {
            if (!_connectionRateLog.TryGetValue(ipKey, out var timestamps))
            {
                timestamps = new List<DateTime>();
                _connectionRateLog[ipKey] = timestamps;
            }

            // 移除过期的记录
            timestamps.RemoveAll(t => t < windowStart);

            // 检查是否超限
            if (timestamps.Count >= _config.ConnectionRateLimit)
            {
                return false;
            }

            // 记录新连接
            timestamps.Add(now);
            return true;
        }
    }

    /// <summary>
    /// 注册新连接
    /// </summary>
    public void RegisterConnection(string connectionId, IPEndPoint remoteEndPoint)
    {
        _activeConnections.TryAdd(connectionId, new ConnectionInfo
        {
            ConnectionId = connectionId,
            RemoteEndPoint = remoteEndPoint,
            ConnectedAt = DateTime.UtcNow
        });

        _logger.Debug($"Connection registered: {connectionId} from {remoteEndPoint.Address}. Total: {_activeConnections.Count}");
    }

    /// <summary>
    /// 注销连接
    /// </summary>
    public void UnregisterConnection(string connectionId)
    {
        if (_activeConnections.TryRemove(connectionId, out var info))
        {
            _logger.Debug($"Connection unregistered: {connectionId}. Total: {_activeConnections.Count}");
        }
    }

    /// <summary>
    /// 生成会话令牌
    /// </summary>
    private static string GenerateSessionToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// 清理过期记录
    /// </summary>
    private void CleanupExpiredRecords(object? state)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-10);

            foreach (var key in _connectionRateLog.Keys.ToList())
            {
                if (_connectionRateLog.TryGetValue(key, out var timestamps))
                {
                    timestamps.RemoveAll(t => t < cutoff);
                    if (timestamps.Count == 0)
                    {
                        _connectionRateLog.TryRemove(key, out _);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error cleaning up expired records: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
        _activeConnections.Clear();
        _connectionRateLog.Clear();
    }

    private class ConnectionInfo
    {
        public string ConnectionId { get; set; } = string.Empty;
        public IPEndPoint RemoteEndPoint { get; set; } = new IPEndPoint(IPAddress.Any, 0);
        public DateTime ConnectedAt { get; set; }
    }
}
