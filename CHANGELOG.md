# Changelog

## 0.0.3 - 2026-04-14

### Fixed

- Reduced misleading socket error logs when TCP/UDP operations are canceled as part of normal timeout, channel close, or disposal flows.
- Improved TCP request-response polling behavior for short-lived industrial device communication scenarios.
- Hardened socket disconnect handling so concurrent close and receive-loop shutdown do not throw during cleanup.

### Tests

- Added TCP client channel regression coverage for close-time receive cancellation without `ERROR` logs.
- Added TCP request-response timeout coverage to verify `TCP receive failed` is not emitted for expected timeout paths.
