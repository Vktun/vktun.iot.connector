using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.Core;

public class ConnectionSettingsValidatorTests
{
    [Fact]
    public void ClientMode_MissingRemoteIp_ShouldFail()
    {
        var result = ConnectionSettingsValidator.ValidateAndNormalize(
            CommunicationType.Tcp,
            ConnectionMode.Client,
            string.Empty,
            502,
            string.Empty,
            0);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ClientMode_MissingRemotePort_ShouldFail()
    {
        var result = ConnectionSettingsValidator.ValidateAndNormalize(
            CommunicationType.Udp,
            ConnectionMode.Client,
            "127.0.0.1",
            0,
            string.Empty,
            0);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ServerMode_MissingLocalPort_ShouldFail()
    {
        var result = ConnectionSettingsValidator.ValidateAndNormalize(
            CommunicationType.Tcp,
            ConnectionMode.Server,
            string.Empty,
            0,
            string.Empty,
            0);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ServerMode_LegacyPort_ShouldNormalizeToLocalPort()
    {
        var device = new DeviceInfo
        {
            DeviceId = "test-device",
            CommunicationType = CommunicationType.Udp,
            ConnectionMode = ConnectionMode.Server,
            Port = 32001,
            LocalPort = 0
        };

        var success = ConnectionSettingsValidator.TryNormalize(device, out var errorMessage);

        Assert.True(success, errorMessage);
        Assert.Equal(32001, device.LocalPort);
        Assert.Equal(0, device.Port);
    }
}

