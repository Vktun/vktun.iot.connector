# Modbus Tunnel Connections Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `TcpOverUdp` and `UdpOverTcp` as real communication channel types for Modbus-oriented tunnel transports.

**Architecture:** Add two `CommunicationType` values, validate them with the existing TCP/UDP endpoint rules, and map them in `CommunicationChannelFactory`. `TcpOverUdpChannel` reuses the existing UDP channel behavior with a different public communication type. `UdpOverTcpChannel` is a new TCP stream channel that frames each logical datagram with a 4-byte big-endian length prefix.

**Tech Stack:** C#/.NET 10, xUnit, existing `ICommunicationChannel` abstractions, `System.Net.Sockets`.

---

## File Structure

- Modify `src/Vktun.IoT.Connector.Core/Enums/Enums.cs`: add `TcpOverUdp` and `UdpOverTcp`.
- Modify `src/Vktun.IoT.Connector.Core/Utils/ConnectionSettingsValidator.cs`: treat the new types as endpoint-based network transports.
- Modify `src/Vktun.IoT.Connector.Communication/Channels/UdpChannel.cs`: allow subclasses to expose a different `CommunicationType`.
- Create `src/Vktun.IoT.Connector.Communication/Channels/TcpOverUdpChannel.cs`: thin dedicated channel over `UdpChannel`.
- Create `src/Vktun.IoT.Connector.Communication/Channels/UdpOverTcpChannel.cs`: length-prefixed TCP channel.
- Modify `src/Vktun.IoT.Connector.Business/Factories/CommunicationChannelFactory.cs`: create the new channel classes.
- Modify `tests/Vktun.IoT.Connector.UnitTests/Core/ConnectionSettingsValidatorTests.cs`: add endpoint validation tests.
- Modify `tests/Vktun.IoT.Connector.UnitTests/Factories/CommunicationChannelFactoryTests.cs`: add factory mapping tests.
- Modify `tests/Vktun.IoT.Connector.UnitTests/Transport/SocketChannelIntegrationTests.cs`: add loopback transport tests.

---

### Task 1: Add Failing Validator Tests

**Files:**
- Test: `tests/Vktun.IoT.Connector.UnitTests/Core/ConnectionSettingsValidatorTests.cs`
- Modify later: `src/Vktun.IoT.Connector.Core/Enums/Enums.cs`
- Modify later: `src/Vktun.IoT.Connector.Core/Utils/ConnectionSettingsValidator.cs`

- [ ] **Step 1: Write the failing tests**

Append these tests to `ConnectionSettingsValidatorTests`:

```csharp
[Fact]
public void TcpOverUdp_ClientMode_ShouldValidateLikeNetworkClient()
{
    var result = ConnectionSettingsValidator.ValidateAndNormalize(
        CommunicationType.TcpOverUdp,
        ConnectionMode.Client,
        "127.0.0.1",
        1502,
        string.Empty,
        0);

    Assert.True(result.IsValid, result.ErrorMessage);
    Assert.NotNull(result.Settings);
    Assert.Equal("127.0.0.1", result.Settings.RemoteIpAddressText);
    Assert.Equal(1502, result.Settings.RemotePort);
}

[Fact]
public void UdpOverTcp_ServerMode_LegacyPort_ShouldNormalizeToLocalPort()
{
    var device = new DeviceInfo
    {
        DeviceId = "udp-over-tcp-server",
        CommunicationType = CommunicationType.UdpOverTcp,
        ConnectionMode = ConnectionMode.Server,
        Port = 2502,
        LocalPort = 0
    };

    var success = ConnectionSettingsValidator.TryNormalize(device, out var errorMessage);

    Assert.True(success, errorMessage);
    Assert.Equal(2502, device.LocalPort);
    Assert.Equal(0, device.Port);
}
```

- [ ] **Step 2: Run the validator tests and verify they fail**

Run:

```powershell
dotnet test tests\Vktun.IoT.Connector.UnitTests\Vktun.IoT.Connector.UnitTests.csproj --filter "FullyQualifiedName~ConnectionSettingsValidatorTests"
```

Expected: compile failure because `CommunicationType.TcpOverUdp` and `CommunicationType.UdpOverTcp` do not exist.

- [ ] **Step 3: Add enum values and endpoint classification**

In `Enums.cs`, change `CommunicationType` to include:

```csharp
public enum CommunicationType
{
    Tcp,
    Udp,
    TcpOverUdp,
    UdpOverTcp,
    Http,
    Mqtt,
    Serial,
    Can,
    FourG,
    NbIoT
}
```

In `ConnectionSettingsValidator.cs`, add:

```csharp
private static bool IsNetworkEndpointTransport(CommunicationType communicationType)
{
    return communicationType is CommunicationType.Tcp
        or CommunicationType.Udp
        or CommunicationType.TcpOverUdp
        or CommunicationType.UdpOverTcp;
}
```

Replace both checks of `communicationType is not CommunicationType.Tcp and not CommunicationType.Udp` with `!IsNetworkEndpointTransport(communicationType)`.

- [ ] **Step 4: Run the validator tests and verify they pass**

Run:

```powershell
dotnet test tests\Vktun.IoT.Connector.UnitTests\Vktun.IoT.Connector.UnitTests.csproj --filter "FullyQualifiedName~ConnectionSettingsValidatorTests"
```

Expected: all `ConnectionSettingsValidatorTests` pass.

---

### Task 2: Add Failing Factory Tests

**Files:**
- Test: `tests/Vktun.IoT.Connector.UnitTests/Factories/CommunicationChannelFactoryTests.cs`
- Modify later: `src/Vktun.IoT.Connector.Business/Factories/CommunicationChannelFactory.cs`
- Create later: `src/Vktun.IoT.Connector.Communication/Channels/TcpOverUdpChannel.cs`
- Create later: `src/Vktun.IoT.Connector.Communication/Channels/UdpOverTcpChannel.cs`

- [ ] **Step 1: Write the failing factory tests**

Add `using Vktun.IoT.Connector.Communication.Channels;` to `CommunicationChannelFactoryTests.cs`, then append:

```csharp
[Fact]
public void CreateChannel_TcpOverUdp_ShouldCreateTcpOverUdpChannel()
{
    var device = new DeviceInfo
    {
        DeviceId = "tcp-over-udp",
        CommunicationType = CommunicationType.TcpOverUdp,
        ConnectionMode = ConnectionMode.Client,
        IpAddress = "127.0.0.1",
        Port = 1502
    };

    var channel = _factory.CreateChannel(device);

    Assert.IsType<TcpOverUdpChannel>(channel);
    Assert.Equal(CommunicationType.TcpOverUdp, channel.CommunicationType);
    Assert.Equal(ConnectionMode.Client, channel.ConnectionMode);
}

[Fact]
public void CreateChannel_UdpOverTcp_ShouldCreateUdpOverTcpChannel()
{
    var device = new DeviceInfo
    {
        DeviceId = "udp-over-tcp",
        CommunicationType = CommunicationType.UdpOverTcp,
        ConnectionMode = ConnectionMode.Server,
        LocalPort = 2502
    };

    var channel = _factory.CreateChannel(device);

    Assert.IsType<UdpOverTcpChannel>(channel);
    Assert.Equal(CommunicationType.UdpOverTcp, channel.CommunicationType);
    Assert.Equal(ConnectionMode.Server, channel.ConnectionMode);
}
```

- [ ] **Step 2: Run the factory tests and verify they fail**

Run:

```powershell
dotnet test tests\Vktun.IoT.Connector.UnitTests\Vktun.IoT.Connector.UnitTests.csproj --filter "FullyQualifiedName~CommunicationChannelFactoryTests"
```

Expected: compile failure because the new channel classes do not exist, or `NotSupportedException` because the factory does not map the new communication types.

- [ ] **Step 3: Make `UdpChannel` expose a configurable communication type**

In `UdpChannel.cs`, add a field:

```csharp
private readonly CommunicationType _communicationType;
```

Change the property:

```csharp
public override CommunicationType CommunicationType => _communicationType;
```

Change the constructor signature:

```csharp
public UdpChannel(
    ConnectionMode mode,
    string localIpAddress,
    int localPort,
    IConfigurationProvider configProvider,
    ILogger logger,
    bool allowAnonymousAcceptedClients = false,
    CommunicationType communicationType = CommunicationType.Udp) : base(configProvider, logger)
```

Assign the field before setting `ChannelId`:

```csharp
_communicationType = communicationType;
```

In `CreateAcceptedDevice`, change:

```csharp
CommunicationType = _communicationType,
```

- [ ] **Step 4: Add `TcpOverUdpChannel`**

Create `src/Vktun.IoT.Connector.Communication/Channels/TcpOverUdpChannel.cs`:

```csharp
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;

namespace Vktun.IoT.Connector.Communication.Channels;

public sealed class TcpOverUdpChannel : UdpChannel
{
    public TcpOverUdpChannel(
        ConnectionMode mode,
        string localIpAddress,
        int localPort,
        IConfigurationProvider configProvider,
        ILogger logger,
        bool allowAnonymousAcceptedClients = false)
        : base(
            mode,
            localIpAddress,
            localPort,
            configProvider,
            logger,
            allowAnonymousAcceptedClients,
            CommunicationType.TcpOverUdp)
    {
        ChannelId = $"TcpOverUdp_{mode}_{localIpAddress}_{localPort}";
    }
}
```

- [ ] **Step 5: Add `UdpOverTcpChannel` class skeleton**

Create `src/Vktun.IoT.Connector.Communication/Channels/UdpOverTcpChannel.cs` with a compiling class:

```csharp
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Communication.Channels;

public sealed class UdpOverTcpChannel : CommunicationChannelBase
{
    private readonly ConnectionMode _mode;

    public override CommunicationType CommunicationType => CommunicationType.UdpOverTcp;
    public override ConnectionMode ConnectionMode => _mode;

    public UdpOverTcpChannel(
        ConnectionMode mode,
        string localIpAddress,
        int localPort,
        IConfigurationProvider configProvider,
        ILogger logger)
        : base(configProvider, logger)
    {
        _mode = mode;
        ChannelId = $"UdpOverTcp_{mode}_{localIpAddress}_{localPort}";
    }

    public override Task<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        _isConnected = true;
        return Task.FromResult(true);
    }

    public override Task CloseAsync()
    {
        _isConnected = false;
        return Task.CompletedTask;
    }

    public override Task<int> SendAsync(string deviceId, byte[] data, CancellationToken cancellationToken = default)
    {
        return SendAsync(deviceId, new ReadOnlyMemory<byte>(data), cancellationToken);
    }

    public override Task<int> SendAsync(string deviceId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    public override async IAsyncEnumerable<ReceivedData> ReceiveAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        throw new NotSupportedException("UdpOverTcpChannel uses event-based data reception (DataReceived event).");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    public override Task<bool> ConnectDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public override Task DisconnectDeviceAsync(string deviceId)
    {
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 6: Map the factory**

In `CommunicationChannelFactory.CreateChannel`, add switch arms:

```csharp
(CommunicationType.TcpOverUdp, _) => new TcpOverUdpChannel(
    device.ConnectionMode,
    device.LocalIpAddress,
    device.LocalPort,
    _configProvider,
    _logger,
    allowAnonymousAcceptedClients: device.ConnectionMode == ConnectionMode.Server),
(CommunicationType.UdpOverTcp, _) => new UdpOverTcpChannel(
    device.ConnectionMode,
    device.LocalIpAddress,
    device.LocalPort,
    _configProvider,
    _logger),
```

- [ ] **Step 7: Run factory tests and verify they pass**

Run:

```powershell
dotnet test tests\Vktun.IoT.Connector.UnitTests\Vktun.IoT.Connector.UnitTests.csproj --filter "FullyQualifiedName~CommunicationChannelFactoryTests"
```

Expected: all `CommunicationChannelFactoryTests` pass.

---

### Task 3: Add Failing TcpOverUdp Transport Test

**Files:**
- Test: `tests/Vktun.IoT.Connector.UnitTests/Transport/SocketChannelIntegrationTests.cs`
- Modify later: `src/Vktun.IoT.Connector.Communication/Channels/UdpChannel.cs`
- Modify later: `src/Vktun.IoT.Connector.Communication/Channels/TcpOverUdpChannel.cs`

- [ ] **Step 1: Write the failing transport test**

Append this test to `SocketChannelIntegrationTests`:

```csharp
[Fact]
public async Task TcpOverUdpClientChannel_ShouldSendAndReceiveDatagram()
{
    using var remote = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    var remotePort = ((IPEndPoint)remote.Client.LocalEndPoint!).Port;

    await using var channel = new TcpOverUdpChannel(ConnectionMode.Client, string.Empty, 0, _configProvider, _logger);
    await channel.OpenAsync();

    var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
    channel.DataReceived += (_, args) => received.TrySetResult(args.Data);

    var device = new DeviceInfo
    {
        DeviceId = "tcp-over-udp-device",
        CommunicationType = CommunicationType.TcpOverUdp,
        ConnectionMode = ConnectionMode.Client,
        IpAddress = "127.0.0.1",
        Port = remotePort
    };

    Assert.True(await channel.ConnectDeviceAsync(device));

    var request = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01, 0x03, 0x00, 0x00, 0x00, 0x01 };
    Assert.Equal(request.Length, await channel.SendAsync(device.DeviceId, request));

    var remoteRequest = await remote.ReceiveAsync();
    Assert.Equal(request, remoteRequest.Buffer);

    var response = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x05, 0x01, 0x03, 0x02, 0x00, 0x2A };
    await remote.SendAsync(response, response.Length, remoteRequest.RemoteEndPoint);

    var actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.Equal(response, actual);
}
```

- [ ] **Step 2: Run the new test and verify it fails if implementation is incomplete**

Run:

```powershell
dotnet test tests\Vktun.IoT.Connector.UnitTests\Vktun.IoT.Connector.UnitTests.csproj --filter "FullyQualifiedName~TcpOverUdpClientChannel_ShouldSendAndReceiveDatagram"
```

Expected before Task 2 implementation: compile failure. Expected after Task 2 implementation: pass because `TcpOverUdpChannel` reuses `UdpChannel`.

- [ ] **Step 3: Fix only what is needed for TcpOverUdp**

If the test fails because `TcpOverUdpChannel` does not preserve the public communication type, confirm that `UdpChannel` assigns `_communicationType` and `CommunicationType` returns it.

- [ ] **Step 4: Run the TcpOverUdp test and verify it passes**

Run:

```powershell
dotnet test tests\Vktun.IoT.Connector.UnitTests\Vktun.IoT.Connector.UnitTests.csproj --filter "FullyQualifiedName~TcpOverUdpClientChannel_ShouldSendAndReceiveDatagram"
```

Expected: pass.

---

### Task 4: Add Failing UdpOverTcp Transport Test

**Files:**
- Test: `tests/Vktun.IoT.Connector.UnitTests/Transport/SocketChannelIntegrationTests.cs`
- Modify: `src/Vktun.IoT.Connector.Communication/Channels/UdpOverTcpChannel.cs`

- [ ] **Step 1: Write the failing transport test**

Append this test to `SocketChannelIntegrationTests`:

```csharp
[Fact]
public async Task UdpOverTcpClientChannel_ShouldSendAndReceiveLengthPrefixedPayload()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var remotePort = ((IPEndPoint)listener.LocalEndpoint).Port;

    await using var channel = new UdpOverTcpChannel(ConnectionMode.Client, string.Empty, 0, _configProvider, _logger);

    var acceptedTask = listener.AcceptTcpClientAsync();
    var device = new DeviceInfo
    {
        DeviceId = "udp-over-tcp-device",
        CommunicationType = CommunicationType.UdpOverTcp,
        ConnectionMode = ConnectionMode.Client,
        IpAddress = "127.0.0.1",
        Port = remotePort
    };

    Assert.True(await channel.ConnectDeviceAsync(device));
    using var accepted = await acceptedTask.WaitAsync(TimeSpan.FromSeconds(3));
    await using var stream = accepted.GetStream();

    var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
    channel.DataReceived += (_, args) => received.TrySetResult(args.Data);

    var payload = new byte[] { 0x10, 0x20, 0x30 };
    Assert.Equal(payload.Length + 4, await channel.SendAsync(device.DeviceId, payload));

    var lengthBuffer = new byte[4];
    await ReadExactlyAsync(stream, lengthBuffer, CancellationToken.None);
    var length = (lengthBuffer[0] << 24) | (lengthBuffer[1] << 16) | (lengthBuffer[2] << 8) | lengthBuffer[3];
    Assert.Equal(payload.Length, length);

    var requestBuffer = new byte[length];
    await ReadExactlyAsync(stream, requestBuffer, CancellationToken.None);
    Assert.Equal(payload, requestBuffer);

    var response = new byte[] { 0x40, 0x50 };
    await stream.WriteAsync(new byte[] { 0x00, 0x00, 0x00, (byte)response.Length });
    await stream.WriteAsync(response);
    await stream.FlushAsync();

    var actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.Equal(response, actual);
}
```

Add this helper inside `SocketChannelIntegrationTests`:

```csharp
private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
{
    var offset = 0;
    while (offset < buffer.Length)
    {
        var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
        if (read == 0)
        {
            throw new EndOfStreamException();
        }

        offset += read;
    }
}
```

- [ ] **Step 2: Run the UdpOverTcp test and verify it fails**

Run:

```powershell
dotnet test tests\Vktun.IoT.Connector.UnitTests\Vktun.IoT.Connector.UnitTests.csproj --filter "FullyQualifiedName~UdpOverTcpClientChannel_ShouldSendAndReceiveLengthPrefixedPayload"
```

Expected: fail because `ConnectDeviceAsync` returns false or `SendAsync` returns 0.

- [ ] **Step 3: Implement `UdpOverTcpChannel`**

Replace the skeleton with a full implementation that:

- Stores `ConnectionMode`, local endpoint, a `TcpClient`, optional `TcpListener`, semaphores, cancellation source, and receive loop task.
- In client `ConnectDeviceAsync`, validates settings, binds an optional local endpoint, connects to the remote endpoint, records a `DeviceConnection`, starts the receive loop, and raises `OnDeviceConnected`.
- In `SendAsync`, writes a 4-byte big-endian length prefix followed by the payload.
- In the receive loop, reads exactly 4 bytes, validates the length, reads the payload, updates connection counters, and raises `OnDataReceived`.
- In `CloseAsync`, cancels the receive loop, disconnects devices, closes the client/listener, and suppresses expected shutdown exceptions.

Use these helper methods in the class:

```csharp
private static byte[] CreateFrame(ReadOnlyMemory<byte> payload)
{
    var frame = new byte[payload.Length + PrefixLength];
    frame[0] = (byte)(payload.Length >> 24);
    frame[1] = (byte)(payload.Length >> 16);
    frame[2] = (byte)(payload.Length >> 8);
    frame[3] = (byte)payload.Length;
    payload.CopyTo(frame.AsMemory(PrefixLength));
    return frame;
}

private static int DecodeLength(byte[] prefix)
{
    return (prefix[0] << 24) | (prefix[1] << 16) | (prefix[2] << 8) | prefix[3];
}
```

- [ ] **Step 4: Run the UdpOverTcp test and verify it passes**

Run:

```powershell
dotnet test tests\Vktun.IoT.Connector.UnitTests\Vktun.IoT.Connector.UnitTests.csproj --filter "FullyQualifiedName~UdpOverTcpClientChannel_ShouldSendAndReceiveLengthPrefixedPayload"
```

Expected: pass.

---

### Task 5: Final Verification

**Files:**
- All files changed by Tasks 1-4.

- [ ] **Step 1: Run all affected focused tests**

Run:

```powershell
dotnet test tests\Vktun.IoT.Connector.UnitTests\Vktun.IoT.Connector.UnitTests.csproj --filter "FullyQualifiedName~ConnectionSettingsValidatorTests|FullyQualifiedName~CommunicationChannelFactoryTests|FullyQualifiedName~SocketChannelIntegrationTests"
```

Expected: all selected tests pass.

- [ ] **Step 2: Run the full unit test project**

Run:

```powershell
dotnet test tests\Vktun.IoT.Connector.UnitTests\Vktun.IoT.Connector.UnitTests.csproj
```

Expected: all unit tests pass.

- [ ] **Step 3: Inspect the final diff**

Run:

```powershell
git diff --stat
git diff -- src tests
```

Expected: diff only includes the enum, connection validator, channel classes, factory mapping, and focused tests.

- [ ] **Step 4: Commit implementation**

Run:

```powershell
git add src tests
git commit -m "feat: add modbus tunnel connection channels"
```

Expected: commit succeeds with the feature implementation.
