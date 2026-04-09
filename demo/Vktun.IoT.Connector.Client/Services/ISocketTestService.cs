using Vktun.IoT.Connector.Client.Models;

namespace Vktun.IoT.Connector.Client.Services;

public interface ISocketTestService
{
    bool IsConnected { get; }
    ConnectionConfig? CurrentConfig { get; }

    Task<bool> ConnectAsync(ConnectionConfig config, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task<int> SendAsync(byte[] data, CancellationToken cancellationToken = default);

    event EventHandler<string>? LogMessage;
    event EventHandler<byte[]>? DataReceived;
}

