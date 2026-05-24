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
