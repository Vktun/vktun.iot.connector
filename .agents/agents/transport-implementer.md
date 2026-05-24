# Transport Implementer

Use this checklist when adding or changing TCP, UDP, Serial, HTTP, MQTT, secure TCP, or tunnel transport behavior.

## Scope

Transport work usually touches:

- `src/Vktun.IoT.Connector.Core/Enums/Enums.cs`
- `src/Vktun.IoT.Connector.Core/Utils/ConnectionSettingsValidator.cs`
- `src/Vktun.IoT.Connector.Core/Interfaces/ICommunicationChannel.cs`
- `src/Vktun.IoT.Connector.Communication/Channels`
- `src/Vktun.IoT.Connector.Driver/Sockets`
- `src/Vktun.IoT.Connector.Serial`
- `src/Vktun.IoT.Connector.Business/Factories/CommunicationChannelFactory.cs`
- `tests/Vktun.IoT.Connector.UnitTests/Transport`
- `tests/Vktun.IoT.Connector.UnitTests/Factories`

## Workflow

1. Write a loopback or focused unit test for the transport behavior.
2. Verify the test fails for the expected reason.
3. Implement the smallest channel or factory change.
4. Verify expected cancellation, remote disconnect, and disposal behavior.
5. Run focused transport tests and the full unit test project.

## Rules

- `CommunicationType` means runtime transport, not protocol parser.
- Endpoint-based transports must be validated through `ConnectionSettingsValidator`.
- Factory mapping must be covered by `CommunicationChannelFactoryTests`.
- Channel implementations should derive from `CommunicationChannelBase` unless extending a local channel is simpler and preserves behavior.
- Event-based receive channels should raise `DataReceived` with payload bytes only.
- Expected shutdown should be quiet or debug-level, not error-level.
- Use dynamic free ports in tests; do not hardcode ports.
- Dispose sockets, streams, cancellation sources, and semaphores cleanly.

## Tunnel Transport Notes

- `TcpOverUdp` uses UDP datagrams to carry complete Modbus TCP payloads.
- `UdpOverTcp` uses TCP streams to carry logical datagrams with a 4-byte big-endian length prefix.
- The length prefix is internal to the transport and should not leak to protocol parsers.

## Verification Commands

```powershell
dotnet test tests\Vktun.IoT.Connector.UnitTests\Vktun.IoT.Connector.UnitTests.csproj --filter "FullyQualifiedName~CommunicationChannelFactoryTests"
dotnet test tests\Vktun.IoT.Connector.UnitTests\Vktun.IoT.Connector.UnitTests.csproj --filter "FullyQualifiedName~SocketChannelIntegrationTests"
dotnet test tests\Vktun.IoT.Connector.UnitTests\Vktun.IoT.Connector.UnitTests.csproj
```

## Done Criteria

- Focused transport tests pass.
- Full unit test project passes.
- Factory and validator behavior are covered.
- No noisy logs on expected disconnect or close.

