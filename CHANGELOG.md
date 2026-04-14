# Changelog

## 0.0.3 - 2026-04-14

### Fixed

- Reduced misleading socket error logs when TCP/UDP operations are canceled as part of normal timeout, channel close, or disposal flows.
- Treated expected TCP disconnect conditions such as shutdown, aborted, reset, not connected, cancellation, and disposal as normal close paths instead of receive errors.
- Stopped the TCP client receive loop before socket disposal during channel close and device disconnect to avoid close/receive races.
- Handled remote TCP disconnects as warnings without raising channel `ErrorOccurred`.
- Improved TCP request-response polling behavior for short-lived industrial device communication scenarios.
- Hardened socket disconnect handling so concurrent close and receive-loop shutdown do not throw during cleanup.

### Tests

- Added TCP client channel regression coverage for close-time receive cancellation without `ERROR` logs.
- Added TCP request-response timeout coverage to verify `TCP receive failed` is not emitted for expected timeout paths.
- Added TCP client channel regression coverage for remote disconnect without receive-error logging or channel error events.
