# Vktun IoT Connector Rules

These rules apply to agent work in this repository.

## Development Discipline

- Use test-first development for behavior changes.
- Do not claim completion without fresh verification output.
- Keep edits scoped to the requested feature or fix.
- Do not revert user changes or unrelated work.
- Use `rg` for searching and focused `dotnet test` filters while developing.

## Layering

- Core contains contracts, enums, models, and utilities only.
- Communication contains runtime channel implementations.
- Driver contains low-level socket/serial wrappers.
- Protocol contains parsers, templates, and parser selection.
- Business composes managers, factories, providers, retry policy, and command execution.
- The root SDK project exposes DI and facade APIs.

## Transport Rules

- A new `CommunicationType` requires endpoint validation, factory mapping, channel behavior, and tests.
- Transport code must distinguish client and server mode.
- Keep logical payload boundaries explicit when using stream transports.
- Do not log expected shutdown as errors.
- Do not hardcode test ports.

## Protocol Rules

- A new `ProtocolType` requires parser implementation, factory registration, template/sample support, and regression tests.
- Do not mix Modbus RTU CRC framing with Modbus TCP MBAP framing.
- Prefer typed `DefinitionJson` config models over legacy parse-rule string blobs.
- Demos are not parser verification.

## Documentation Rules

- Do not overstate production readiness.
- Mark experimental, limited, opt-in, and demo-only features honestly.
- Do not bulk-fix mojibake or line endings in unrelated docs.
- Keep project-agent docs ASCII unless there is a clear need for Chinese text or symbols.

## Required Verification

Run the relevant focused tests, then the full relevant test project.

Common commands:

```powershell
dotnet test tests\Vktun.IoT.Connector.UnitTests\Vktun.IoT.Connector.UnitTests.csproj
dotnet test tests\Vktun.IoT.Connector.ProtocolTests\Vktun.IoT.Connector.ProtocolTests.csproj
git diff --check
```

If a command cannot be run, report why and state the residual risk.

