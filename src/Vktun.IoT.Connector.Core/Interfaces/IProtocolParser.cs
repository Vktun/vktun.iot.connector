using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Core.Interfaces
{
    public interface IProtocolParser
    {
        ProtocolType Type { get; }
        string Name { get; }
        string Version { get; }
        string Description { get; }
        string Vendor { get; }
        string[] SupportedDeviceModels { get; }
        string Author { get; }
        ParserStatus Status { get; }

        List<DeviceData> Parse(byte[] rawData, ProtocolConfig config);
        List<DeviceData> Parse(ReadOnlySpan<byte> rawData, ProtocolConfig config);
        byte[] Pack(DeviceData data, ProtocolConfig config);
        byte[] Pack(DeviceCommand command, ProtocolConfig config);
        bool Validate(byte[] rawData, ProtocolConfig config);
    }

    public enum ParserStatus
    {
        Experimental,
        Stable,
        Deprecated
    }

    public interface IProtocolParserFactory
    {
        IProtocolParser? GetParser(ProtocolType type);
        IProtocolParser? GetParser(string protocolId);
        IProtocolParser? GetParser(ProtocolType type, string version, string deviceModel);
        void RegisterParser(IProtocolParser parser);
        void RegisterParser(string protocolId, IProtocolParser parser);
        void UnregisterParser(ProtocolType type);
        void UnregisterParser(string protocolId);
        IEnumerable<ProtocolType> GetSupportedProtocols();
        IEnumerable<ParserDescriptor> GetAllParserDescriptors();
        void LoadPluginParsers(string pluginDirectory);
    }

    public class ParserDescriptor
    {
        public string ProtocolId { get; set; } = string.Empty;
        public ProtocolType ProtocolType { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty;
        public string[] SupportedDeviceModels { get; set; } = Array.Empty<string>();
        public string Author { get; set; } = string.Empty;
        public ParserStatus Status { get; set; } = ParserStatus.Stable;
    }
}
