using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Core.Interfaces;

/// <summary>
/// A device-level, request/response Modbus TCP transport. Every physical endpoint
/// owns one serialized connection; callers never own a socket or a receive loop.
/// </summary>
public interface IPersistentModbusTcpTransport : IAsyncDisposable
{
    /// <summary>Establishes or reuses the physical endpoint connection without sending a Modbus frame.</summary>
    Task<PersistentModbusTcpResult> ProbeAsync(
        PersistentModbusTcpRequest request,
        CancellationToken cancellationToken = default);

    Task<PersistentModbusTcpResult> SendAsync(
        PersistentModbusTcpRequest request,
        CancellationToken cancellationToken = default);

    Task ShutdownAsync();
}
