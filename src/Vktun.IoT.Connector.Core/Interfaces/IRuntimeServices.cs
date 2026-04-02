using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Core.Interfaces;

public interface ICommunicationChannelFactory
{
    ICommunicationChannel CreateChannel(DeviceInfo device);
}

public interface IDeviceCommandExecutor
{
    Task<bool> ConnectAsync(DeviceInfo device, CancellationToken cancellationToken = default);
    Task DisconnectAsync(string deviceId, CancellationToken cancellationToken = default);
    Task<CommandResult> ExecuteAsync(DeviceCommand command, DeviceInfo device, CancellationToken cancellationToken = default);
    Task<ProtocolConfig?> GetProtocolConfigAsync(DeviceInfo device, CancellationToken cancellationToken = default);

    event EventHandler<DataReceivedEventArgs>? DataReceived;
}
