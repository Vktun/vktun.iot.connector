using Moq;
using Xunit;
using Vktun.IoT.Connector.Business.Services;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Protocol.Parsers;

namespace Vktun.IoT.Connector.UnitTests.Services;

public class DeviceCommandExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReassembleSplitModbusTcpResponseBeforeParsing()
    {
        var device = new DeviceInfo
        {
            DeviceId = "modbus-device",
            ChannelId = "modbus-channel",
            CommunicationType = CommunicationType.Tcp,
            ConnectionMode = ConnectionMode.Client,
            IpAddress = "127.0.0.1",
            Port = 502,
            ProtocolId = "modbus-tcp",
            ProtocolType = ProtocolType.ModbusTcp
        };
        var protocolConfig = new ProtocolConfig
        {
            ProtocolId = device.ProtocolId,
            ProtocolName = "Modbus TCP",
            ProtocolType = ProtocolType.ModbusTcp
        };
        protocolConfig.SetDefinition(new ModbusConfig
        {
            ModbusType = ModbusType.Tcp,
            SlaveId = 1,
            Points =
            {
                new ModbusPointConfig
                {
                    PointName = "register-0",
                    RegisterType = ModbusRegisterType.HoldingRegister,
                    Address = 0,
                    DataType = DataType.UInt16
                }
            }
        });
        device.ExtendedProperties["ProtocolConfig"] = protocolConfig;

        var channel = new Mock<ICommunicationChannel>();
        channel.SetupGet(value => value.ChannelId).Returns(device.ChannelId);
        channel.SetupGet(value => value.CommunicationType).Returns(CommunicationType.Tcp);
        channel.SetupGet(value => value.ConnectionMode).Returns(ConnectionMode.Client);
        channel.Setup(value => value.OpenAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        channel.Setup(value => value.ConnectDeviceAsync(device, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        channel.Setup(value => value.SendAsync(device.DeviceId, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Callback((string _, byte[] _, CancellationToken _) =>
            {
                channel.Raise(
                    value => value.DataReceived += null,
                    new DataReceivedEventArgs { DeviceId = device.DeviceId, Data = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00 } });
                channel.Raise(
                    value => value.DataReceived += null,
                    new DataReceivedEventArgs { DeviceId = device.DeviceId, Data = new byte[] { 0x05, 0x01, 0x03, 0x02, 0x00, 0x2A } });
            })
            .Returns((string _, byte[] request, CancellationToken _) => Task.FromResult(request.Length));

        var parser = new ModbusTcpParser(Mock.Of<ILogger>());
        var channelFactory = new Mock<ICommunicationChannelFactory>();
        channelFactory.Setup(value => value.CreateChannel(device)).Returns(channel.Object);
        var parserFactory = new Mock<IProtocolParserFactory>();
        parserFactory.Setup(value => value.GetParser(device.ProtocolId)).Returns(parser);
        var configurationProvider = new Mock<IConfigurationProvider>();
        await using var executor = new DeviceCommandExecutor(
            channelFactory.Object,
            parserFactory.Object,
            configurationProvider.Object,
            Mock.Of<ILogger>());

        Assert.True(await executor.ConnectAsync(device));

        var result = await executor.ExecuteAsync(new DeviceCommand
        {
            DeviceId = device.DeviceId,
            CommandName = "ReadHoldingRegisters",
            Data = new byte[] { 0x00 },
            Timeout = 1000
        }, device);

        Assert.True(result.Success);
        Assert.NotNull(result.ParsedData);
        Assert.Single(result.ParsedData.DataItems);
        Assert.Equal(42d, result.ParsedData.DataItems[0].Value);
    }
}
