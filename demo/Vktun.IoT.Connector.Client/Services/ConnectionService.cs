using System.Collections.Concurrent;
using Vktun.IoT.Connector.Client.Models;

namespace Vktun.IoT.Connector.Client.Services;

public class ConnectionService : IConnectionService
{
    private readonly ConcurrentDictionary<string, ConnectionConfig> _connections = new();
    
    public event EventHandler<ConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

    public bool IsConnected(string connectionId)
    {
        return _connections.TryGetValue(connectionId, out var config) && config.IsConnected;
    }

    public async Task<bool> ConnectAsync(ConnectionConfig config)
    {
        try
        {
            var connectionId = $"{config.ProtocolType}_{config.IpAddress}_{config.Port}";
            
            config.IsConnected = true;
            config.LastConnectTime = DateTime.Now;
            
            _connections.AddOrUpdate(connectionId, config, (key, oldValue) => config);
            
            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs
            {
                ConnectionId = connectionId,
                IsConnected = true,
                Message = $"连接成功: {config.IpAddress}:{config.Port}",
                Timestamp = DateTime.Now
            });
            
            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs
            {
                ConnectionId = $"{config.ProtocolType}_{config.IpAddress}_{config.Port}",
                IsConnected = false,
                Message = $"连接失败: {ex.Message}",
                Timestamp = DateTime.Now
            });
            
            return false;
        }
    }

    public async Task DisconnectAsync(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out var config))
        {
            config.IsConnected = false;
            config.LastDisconnectTime = DateTime.Now;
            
            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs
            {
                ConnectionId = connectionId,
                IsConnected = false,
                Message = $"已断开连接: {config.IpAddress}:{config.Port}",
                Timestamp = DateTime.Now
            });
        }
        
        await Task.CompletedTask;
    }

    public async Task DisconnectAllAsync()
    {
        foreach (var kvp in _connections)
        {
            await DisconnectAsync(kvp.Key);
        }
    }
}
