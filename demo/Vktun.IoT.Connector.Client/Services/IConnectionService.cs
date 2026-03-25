using Vktun.IoT.Connector.Client.Models;

namespace Vktun.IoT.Connector.Client.Services;

public interface IConnectionService
{
    bool IsConnected(string connectionId);
    Task<bool> ConnectAsync(ConnectionConfig config);
    Task DisconnectAsync(string connectionId);
    Task DisconnectAllAsync();
    event EventHandler<ConnectionStatusChangedEventArgs>? ConnectionStatusChanged;
}

public class ConnectionStatusChangedEventArgs : EventArgs
{
    public string ConnectionId { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public string? Message { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
