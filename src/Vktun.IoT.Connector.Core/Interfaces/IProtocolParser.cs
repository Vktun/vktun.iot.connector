using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Core.Interfaces;

public interface IProtocolParser
{
    ProtocolType Type { get; }
    string Name { get; }
    
    List<DeviceData> Parse(byte[] rawData, ProtocolConfig config);
    List<DeviceData> Parse(ReadOnlySpan<byte> rawData, ProtocolConfig config);
    byte[] Pack(DeviceData data, ProtocolConfig config);
    byte[] Pack(DeviceCommand command, ProtocolConfig config);
    bool Validate(byte[] rawData, ProtocolConfig config);
}

public interface IProtocolParserFactory
{
    IProtocolParser? GetParser(ProtocolType type);
    IProtocolParser? GetParser(string protocolId);
    void RegisterParser(IProtocolParser parser);
    void RegisterParser(string protocolId, IProtocolParser parser);
    void UnregisterParser(ProtocolType type);
    void UnregisterParser(string protocolId);
    IEnumerable<ProtocolType> GetSupportedProtocols();
}
