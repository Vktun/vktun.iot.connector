using Vktun.IoT.Connector.Communication.Channels;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Serial.Channels;

namespace Vktun.IoT.Connector.Business.Factories;

public class CommunicationChannelFactory : ICommunicationChannelFactory
{
    private readonly IConfigurationProvider _configProvider;
    private readonly ILogger _logger;

    public CommunicationChannelFactory(IConfigurationProvider configProvider, ILogger logger)
    {
        _configProvider = configProvider;
        _logger = logger;
    }

    public ICommunicationChannel CreateChannel(DeviceInfo device)
    {
        return (device.CommunicationType, device.ConnectionMode) switch
        {
            (CommunicationType.Tcp, ConnectionMode.Client) => new TcpClientChannel(_configProvider, _logger),
            (CommunicationType.Tcp, ConnectionMode.Server) => new TcpServerChannel(device.Port, _configProvider, _logger),
            (CommunicationType.Udp, _) => new UdpChannel(device.Port, _configProvider, _logger),
            (CommunicationType.Serial, _) => new SerialChannel(device.SerialPort, device.BaudRate, _configProvider, _logger),
            _ => throw new NotSupportedException($"Unsupported channel type: {device.CommunicationType}/{device.ConnectionMode}")
        };
    }
}
