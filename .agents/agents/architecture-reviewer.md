# Architecture Reviewer

Use this checklist when reviewing changes to repository structure, project boundaries, public SDK behavior, or maturity claims.

## Review Goals

- Preserve the layered SDK architecture.
- Prevent demo or experimental code from being treated as production capability.
- Keep transport, parser, business orchestration, and facade responsibilities separate.

## Checklist

1. Confirm dependency direction.
   - `Core` should not depend on implementation projects.
   - `Communication`, `Protocol`, and `Business` may depend on `Core`.
   - The root SDK facade composes services and exposes public APIs.

2. Check ownership of the change.
   - Transport behavior belongs in `Communication` or `Serial`.
   - Parser behavior belongs in `Protocol`.
   - Device orchestration belongs in `Business`.
   - DI/public entrypoints belong in `src/Vktun.IoT.Connector`.

3. Check capability wording.
   - Verified: supported by build/test coverage and implemented runtime path.
   - Limited usable: code exists but needs field validation or has known gaps.
   - Experimental: present but not production-ready.
   - Opt-in only: not part of default `AddVktunIoTConnector` runtime path.

4. Check tests.
   - Behavior changes need focused tests.
   - Parser changes need protocol tests or sample replay coverage.
   - Factory/DI changes need factory/DI tests.
   - Transport changes need loopback tests where possible.

5. Check blast radius.
   - No unrelated README encoding cleanup.
   - No broad refactors hidden in feature work.
   - No new abstraction unless it removes real duplication or follows local patterns.

## Common Findings

- A new protocol parser was added but not registered in `ProtocolParserFactory`.
- A new transport enum was added without `ConnectionSettingsValidator` support.
- Demo UI code claims a capability that the SDK runtime path does not verify.
- Optional cloud connectors are registered as default services without explicit opt-in.

