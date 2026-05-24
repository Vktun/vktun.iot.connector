---
name: vktun-iot-connector
description: Use when working in the Vktun.IoT.Connector repository on architecture, transports, protocol parsers, DI registration, DeviceMock, regression tests, documentation maturity claims, or SDK layering decisions.
---

# Vktun IoT Connector

## Overview

This skill keeps agents aligned with the repository architecture and the maturity of the current implementation. The core habit is to separate transport, protocol parsing, business orchestration, and public SDK facade work.

Read `AGENTS.md` first for the current project map. Use this skill when a task touches code under `src/`, `demo/`, `tests/`, protocol templates, communication channels, or capability documentation.

## Quick Architecture Map

| Area | Responsibility | Common files |
| --- | --- | --- |
| Core | Contracts, enums, models, validators | `Core/Enums`, `Core/Interfaces`, `Core/Models`, `Core/Utils` |
| Communication | Runtime channels and socket-facing behavior | `Communication/Channels`, `Communication/Mqtt` |
| Driver | Low-level socket/serial wrappers | `Driver/Sockets`, `Serial/Drivers` |
| Protocol | Parsers, parser factory, templates | `Protocol/Parsers`, `Protocol/Factories`, `Protocol/Templates` |
| Business | Managers, command execution, channel factory, providers | `Business/Managers`, `Business/Factories`, `Business/Services` |
| SDK facade | Public collector and DI extensions | `src/Vktun.IoT.Connector` |
| Tests | Unit, transport, protocol regression | `tests/Vktun.IoT.Connector.*` |
| Demo | WPF/debug/mock tooling | `demo/` |

## Transport Work

When changing communication channels:

1. Add or update tests in `SocketChannelIntegrationTests` or focused transport tests.
2. Keep public behavior behind `ICommunicationChannel`.
3. Update `CommunicationType` only when it represents a real runtime transport.
4. Update `ConnectionSettingsValidator` for endpoint-based transports.
5. Update `CommunicationChannelFactory` and factory tests.
6. Verify cancellation, disposal, and expected remote disconnect behavior.

Transport and protocol are distinct. A Modbus TCP payload can be carried by different `CommunicationType` values; the parser remains `ProtocolType.ModbusTcp`.

## Protocol Work

When changing protocol parsing:

1. Add or update parser tests and protocol regression tests before changing parser code.
2. Keep parser frame shape explicit: Modbus RTU uses CRC; Modbus TCP uses MBAP; custom protocols use configured frame definitions.
3. Update parser factory tests when adding parser selection behavior.
4. Update templates and sample frames together.
5. Do not rely only on demos for validation.

## DI And Facade Work

When changing public SDK registration:

1. Update `VktunIoTConnectorServiceCollectionExtensions`.
2. Add or update dependency injection tests.
3. Keep optional connectors opt-in unless the existing public contract says otherwise.
4. Avoid registering experimental protocols as production defaults.

## Documentation And Claims

The README and docs include known encoding damage. Do not bulk-rewrite unrelated docs. When updating docs:

- Use conservative maturity wording.
- Mark demo-only, experimental, limited, and opt-in paths clearly.
- Link to tests or implemented code paths when claiming verified support.
- Keep new project-agent docs in ASCII unless there is a clear reason not to.

## Verification Checklist

Before saying work is complete:

- Focused test for the changed behavior has failed first and then passed.
- Full relevant test project passed.
- Protocol work ran protocol regression tests.
- Factory/DI changes have factory/DI tests.
- `git diff --check` has no whitespace errors.
- Final summary names any warnings or skipped verification.

## Role Checklists

Use these local role files for deeper review:

- `.agents/agents/architecture-reviewer.md`
- `.agents/agents/protocol-implementer.md`
- `.agents/agents/transport-implementer.md`

