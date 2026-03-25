using Vktun.IoT.Connector.Client.Models;
using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.Client.Services;

public interface IProtocolTestService
{
    Task<DeviceTestResult> ReadAsync(ProtocolType protocolType, string address, DataType dataType, Dictionary<string, object>? parameters = null);
    Task<DeviceTestResult> WriteAsync(ProtocolType protocolType, string address, object value, DataType dataType, Dictionary<string, object>? parameters = null);
    Task<BatchTestResult> BatchReadAsync(ProtocolType protocolType, List<ProtocolTestData> testDataList);
    Task<BatchTestResult> BatchWriteAsync(ProtocolType protocolType, List<ProtocolTestData> testDataList);
    bool IsConnected(ProtocolType protocolType);
    Task<bool> ConnectAsync(ConnectionConfig config);
    Task DisconnectAsync(ProtocolType protocolType);
    event EventHandler<string>? LogMessage;
}
