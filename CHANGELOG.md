# Changelog

## 0.0.4 - 2026-04-15

### Added

- Added HTTP client channel support backed by `IHttpClientFactory` for high-concurrency HTTP device/API access.
- Added MQTT publish/subscribe runtime support, typed MQTT client APIs, QoS/retain handling, reconnect handling, and subscription recovery coverage.
- Added configurable Redis data cache support while preserving memory cache as the default when Redis is not enabled.
- Added data persistence abstractions and storage backends for memory, file, SQLite, and external persistence integration.
- Added protocol request/response sample replay library and regression tests for Modbus TCP/RTU frames.
- Added protocol template compatibility tests covering source templates, root templates, and demo protocol templates.
- Added NuGet package planning, installation matrix, template field documentation, protocol access guide, and device onboarding documentation.

### Changed

- Improved resource monitoring with device/protocol metrics, slow request tracking, timeout/error/reconnect statistics, and structured diagnostics fields.
- Improved protocol template loading compatibility for differently cased JSON fields such as `protocolType` and `ProtocolType`.
- Updated package metadata and README content for HTTP/MQTT, package dependency relationships, and recommended installation paths.

### Tests

- Expanded protocol regression tests to 28 cases with request packing, response validation, parsing, and template compatibility coverage.
- Expanded unit tests to cover HTTP/MQTT exceptional paths, MQTT topic filtering, resource monitoring, data providers, DI registration, and communication factory behavior.

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
