using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.Core.Models;

/// <summary>
/// Defines the byte stream framing expected by a Modbus device reached over TCP.
/// </summary>
public enum PersistentModbusTcpWireFormat
{
    Mbap,
    RtuOverTcp,
    RtuOverTcpWithCrc
}

public enum PersistentModbusTcpOutcome
{
    Succeeded,
    ConnectFailed,
    SendFailed,
    RemoteClosed,
    ResponseTimeout,
    ProtocolError,
    Cancelled
}

/// <summary>
/// One serialized Modbus request. The endpoint key deliberately excludes StationId,
/// so stations behind the same TCP gateway share one physical connection.
/// </summary>
public sealed class PersistentModbusTcpRequest
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 502;
    public string? LocalAddress { get; init; }
    public int LocalPort { get; init; }
    public PersistentModbusTcpWireFormat WireFormat { get; init; } = PersistentModbusTcpWireFormat.Mbap;
    public required byte[] Payload { get; init; }
    public int TimeoutMs { get; init; } = 5000;
}

public sealed class PersistentModbusTcpResult
{
    public PersistentModbusTcpOutcome Outcome { get; init; }
    public byte[]? Response { get; init; }
    public string? ErrorMessage { get; init; }
    public bool Sent { get; init; }
    public long ConnectElapsedMs { get; init; }
    public long SendElapsedMs { get; init; }
    public long ReceiveElapsedMs { get; init; }
    public long TotalElapsedMs { get; init; }
    public bool Succeeded => Outcome == PersistentModbusTcpOutcome.Succeeded;
}
