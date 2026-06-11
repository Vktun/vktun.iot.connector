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

    [Theory]
    [InlineData(CommunicationType.Can)]
    [InlineData(CommunicationType.FourG)]
    [InlineData(CommunicationType.NbIoT)]
    public void EndpointBackedTransports_ClientMode_ShouldRequireRemoteEndpoint(CommunicationType communicationType)
    {
        var result = ConnectionSettingsValidator.ValidateAndNormalize(
            communicationType,
            ConnectionMode.Client,
            "127.0.0.1",
            15000,
            string.Empty,
            0);

        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.NotNull(result.Settings);
        Assert.Equal(communicationType, result.Settings.CommunicationType);
        Assert.Equal("127.0.0.1", result.Settings.RemoteIpAddressText);
        Assert.Equal(15000, result.Settings.RemotePort);
    }

    [Theory]
    [InlineData(CommunicationType.Can)]
    [InlineData(CommunicationType.FourG)]
    [InlineData(CommunicationType.NbIoT)]
    public void EndpointBackedTransports_ServerMode_LegacyPort_ShouldNormalizeToLocalPort(CommunicationType communicationType)
    {
        var device = new DeviceInfo
        {
            DeviceId = $"{communicationType}-server",
            CommunicationType = communicationType,
            ConnectionMode = ConnectionMode.Server,
            Port = 26000
        };

        var success = ConnectionSettingsValidator.TryNormalize(device, out var errorMessage);

        Assert.True(success, errorMessage);
        Assert.Equal(26000, device.LocalPort);
        Assert.Equal(0, device.Port);
    }
}

