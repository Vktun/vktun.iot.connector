# Modbus Tunnel Connections Design

## Goal

Add two real Modbus-capable transport choices:

- `TcpOverUdp`: send and receive Modbus TCP frames over UDP datagrams.
- `UdpOverTcp`: send and receive datagram-style payloads over a TCP stream.

The new choices are connection modes, not aliases. Existing `Tcp`, `Udp`, `ModbusTcp`, and `ModbusRtu` behavior stays unchanged.

## Current Context

Connection selection is currently driven by `DeviceInfo.CommunicationType` and `DeviceInfo.ConnectionMode`. `CommunicationChannelFactory` maps those values to concrete channels. `ConnectionSettingsValidator` validates only `Tcp` and `Udp` as network endpoint types. Modbus TCP and RTU are parser-level protocol types; they do not create transport channels by themselves.

Existing channel behavior:

- `TcpClientChannel` and `TcpServerChannel` carry stream data.
- `UdpChannel` carries datagram data and can operate in client or server mode.
- The factory already uses the same device endpoint fields for TCP and UDP: `IpAddress`, `Port`, `LocalIpAddress`, and `LocalPort`.

## Recommended Approach

Use dedicated channel classes and enum values.

`CommunicationType` gains:

- `TcpOverUdp`
- `UdpOverTcp`

`ConnectionSettingsValidator` treats both new values as network endpoint types, using the same client/server validation rules as TCP and UDP.

`CommunicationChannelFactory` creates:

- `TcpOverUdpChannel` for `CommunicationType.TcpOverUdp`
- `UdpOverTcpChannel` for `CommunicationType.UdpOverTcp`

## Channel Semantics

`TcpOverUdpChannel` uses UDP as the physical transport. Each `SendAsync` call sends one complete payload as a UDP datagram. The channel preserves datagram boundaries and raises `DataReceived` once per datagram. This supports Modbus TCP Application Data Unit payloads without requiring the TCP socket transport.

`UdpOverTcpChannel` uses TCP as the physical transport. Since TCP is a byte stream, each payload is framed with a 4-byte big-endian length prefix before being written to the stream. The receive loop reconstructs full payloads and raises `DataReceived` once per reconstructed payload. The length prefix is internal to the tunnel channel and is not exposed to protocol parsers.

Both channels support `ConnectionMode.Client` and `ConnectionMode.Server` where practical:

- Client mode actively connects to a remote endpoint.
- Server mode listens on a local endpoint and binds the configured device to the first accepted peer, following the existing TCP and UDP server patterns.

## Data Flow

For `TcpOverUdp`:

1. Caller packs a Modbus TCP command using the existing parser.
2. Channel sends the packed bytes as a UDP datagram.
3. Remote endpoint replies with a UDP datagram.
4. Channel raises `DataReceived` with the datagram payload.
5. Existing Modbus TCP parser consumes the payload.

For `UdpOverTcp`:

1. Caller passes a datagram-style payload to the channel.
2. Channel writes `[length][payload]` to the TCP stream.
3. Receive loop reads exactly one length-prefixed payload.
4. Channel raises `DataReceived` with only the payload bytes.
5. Existing upper-layer code sees one logical received message per send.

## Error Handling

Endpoint validation failures return the same style of messages as existing TCP and UDP validation.

`TcpOverUdpChannel` reports send and receive socket errors through `ErrorOccurred`, then continues listening when the socket can remain open.

`UdpOverTcpChannel` reports framing errors through `ErrorOccurred`. Invalid frame lengths close the affected connection to avoid stream desynchronization.

Both channels cancel pending receive loops on `CloseAsync` and suppress expected shutdown exceptions.

## Testing

Tests will be added before implementation.

Factory tests:

- `CreateChannel_TcpOverUdp_ShouldCreateTcpOverUdpChannel`
- `CreateChannel_UdpOverTcp_ShouldCreateUdpOverTcpChannel`

Validator tests:

- `TcpOverUdp_ClientMode_ShouldValidateLikeNetworkClient`
- `UdpOverTcp_ServerMode_LegacyPort_ShouldNormalizeToLocalPort`

Transport tests:

- `TcpOverUdpClientChannel_ShouldSendAndReceiveDatagram`
- `UdpOverTcpClientChannel_ShouldSendAndReceiveLengthPrefixedPayload`

The transport tests use loopback sockets and dynamic free ports, matching the existing `SocketChannelIntegrationTests` style.

## Out Of Scope

This change does not add a new Modbus parser type. It does not change Modbus TCP MBAP framing, Modbus RTU CRC behavior, or demo UI screens. Demo UI support can be added later after the core transport behavior is covered.
