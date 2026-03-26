using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.DeviceMock.Services;

public interface IDeviceSimulator
{
    string DeviceId { get; }
    ProtocolType ProtocolType { get; }
    bool IsRunning { get; }
    
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
    void SetDataPoint(string address, object value);
    object GetDataPoint(string address);
}
