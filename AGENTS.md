# Vktun.IoT.Connector Agent Guide

This file is the project-level entry point for coding agents working in this repository.

## Project Shape

`Vktun.IoT.Connector` is a .NET 10 industrial device data acquisition SDK. Treat it as a layered SDK, not as a single demo app.

Primary layers:

- `src/Vktun.IoT.Connector.Core`: shared enums, interfaces, models, protocol config models, validation utilities.
- `src/Vktun.IoT.Connector.Communication`: runtime communication channels such as TCP, UDP, HTTP, MQTT, secure TCP, and tunnel transports.
- `src/Vktun.IoT.Connector.Driver`: low-level socket and hardware driver wrappers.
- `src/Vktun.IoT.Connector.Protocol`: protocol parsers, parser factory, and protocol templates.
- `src/Vktun.IoT.Connector.Business`: device/session/heartbeat managers, command execution, retry policies, channel factory, providers, cloud connectors.
- `src/Vktun.IoT.Connector.Concurrency`: scheduler, queues, and resource monitoring.
- `src/Vktun.IoT.Connector.Serial`: independent serial package and serial channel implementation.
- `src/Vktun.IoT.Connector`: public SDK facade and DI registration.
- `demo/`: WPF client, console demos, and DeviceMock tooling. Demo code is not proof of production readiness.
- `tests/`: unit tests and protocol regression tests. Add or update tests with behavior changes.

## Capability Truth

Do not overstate protocol maturity. The verified paths focus on Modbus RTU/TCP, HTTP/MQTT SDK integration, TCP/UDP/Serial transport basics, and custom protocol parsing. S7, IEC104, OPC UA, BACnet, CANopen, cloud connectors, and demo pages may be limited, opt-in, experimental, or only partially validated. Check docs before claiming production support.

Current docs contain mojibake/encoding damage in several markdown files. Do not mass-reformat or re-encode those files while doing unrelated work.

## Architecture Rules

- Keep dependency direction clean: Core has contracts/models only; Communication and Protocol depend on Core; Business composes channels/parsers/managers; the root SDK project exposes facade and DI.
- Protocol parsing and transport are separate concerns. `ProtocolType.ModbusTcp` is a parser choice; `CommunicationType.Tcp`, `Udp`, `TcpOverUdp`, or `UdpOverTcp` is a transport choice.
- Add new `CommunicationType` values only with endpoint validation, factory mapping, channel implementation, and transport tests.
- Add new `ProtocolType` values only with parser registration, config/template support, sample frames, and regression tests.
- Do not add broad abstractions unless they reduce real duplication or match existing factory/channel/parser patterns.

## Implementation Rules

- Use test-first changes for behavior: write the focused test, watch it fail, implement the minimal production code, then run focused and full tests.
- Prefer existing interfaces and event patterns: `ICommunicationChannel`, `CommunicationChannelBase`, `DataReceived`, `DataSent`, `DeviceConnected`, and `ErrorOccurred`.
- Preserve cancellation and disposal semantics in transport code. Shutdown should not log noisy expected errors.
- For socket code, bind local endpoints only when configured, validate IPv4 endpoint fields through `ConnectionSettingsValidator`, and use dynamic free ports in tests.
- For Modbus changes, keep RTU CRC behavior and TCP MBAP framing explicit. Do not conflate RTU and TCP parser frame shapes.
- For DI changes, update service registration tests under `tests/Vktun.IoT.Connector.UnitTests/DependencyInjection`.
- Keep public examples conservative and tested.

## Test Commands

Use focused tests while developing:

```powershell
dotnet test tests\Vktun.IoT.Connector.UnitTests\Vktun.IoT.Connector.UnitTests.csproj --filter "FullyQualifiedName~CommunicationChannelFactoryTests"
dotnet test tests\Vktun.IoT.Connector.UnitTests\Vktun.IoT.Connector.UnitTests.csproj --filter "FullyQualifiedName~SocketChannelIntegrationTests"
dotnet test tests\Vktun.IoT.Connector.ProtocolTests\Vktun.IoT.Connector.ProtocolTests.csproj
```

Before finishing SDK work, run:

```powershell
dotnet test tests\Vktun.IoT.Connector.UnitTests\Vktun.IoT.Connector.UnitTests.csproj
dotnet test tests\Vktun.IoT.Connector.ProtocolTests\Vktun.IoT.Connector.ProtocolTests.csproj
```

If a test suite fails because of pre-existing environmental issues, report the exact command and failure. Do not claim success from partial evidence.

## Project-Specific Skill And Agents

Use `.agents/skills/vktun-iot-connector/SKILL.md` when work touches architecture, protocols, transports, DI, tests, DeviceMock, or maturity claims.

Use role files in `.agents/agents/` as checklists:

- `architecture-reviewer.md`
- `protocol-implementer.md`
- `transport-implementer.md`

Use `.agents/rules/vktun-iot-connector-rules.md` as the compact rules reference.

