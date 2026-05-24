# Protocol Implementer

Use this checklist when adding or changing industrial protocol parsing, packing, templates, or sample replay tests.

## Scope

Protocol work usually touches:

- `src/Vktun.IoT.Connector.Core/Models/*Models.cs`
- `src/Vktun.IoT.Connector.Core/Enums/Enums.cs`
- `src/Vktun.IoT.Connector.Protocol/Parsers`
- `src/Vktun.IoT.Connector.Protocol/Factories/ProtocolParserFactory.cs`
- `src/Vktun.IoT.Connector.Protocol/Templates`
- `tests/Vktun.IoT.Connector.UnitTests/Protocols`
- `tests/Vktun.IoT.Connector.ProtocolTests`

## Workflow

1. Start with a failing parser or protocol regression test.
2. Add minimal parser behavior to pass the test.
3. Add template/sample data only when the parser consumes it.
4. Register the parser in `ProtocolParserFactory` if it is externally selectable.
5. Run focused parser tests and protocol tests.

## Rules

- Keep frame format boundaries explicit.
- Modbus RTU frames include CRC; Modbus TCP frames include MBAP; do not silently accept the wrong shape.
- Prefer `DefinitionJson` and strongly typed config models over ad hoc string keys in `ParseRules`.
- Preserve `ReadOnlySpan<byte>` parsing overloads where present.
- Log parse failures, but avoid throwing from public parse paths unless the existing parser contract expects it.
- Sample frames should cover normal, short, invalid checksum/length, and exception response paths when practical.

## Verification Commands

```powershell
dotnet test tests\Vktun.IoT.Connector.UnitTests\Vktun.IoT.Connector.UnitTests.csproj --filter "FullyQualifiedName~Protocols"
dotnet test tests\Vktun.IoT.Connector.ProtocolTests\Vktun.IoT.Connector.ProtocolTests.csproj
```

## Done Criteria

- Parser tests pass.
- Protocol regression tests pass.
- Templates match runtime config models.
- Documentation does not overstate maturity.

