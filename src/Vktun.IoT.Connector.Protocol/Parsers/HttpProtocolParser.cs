using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Protocol.Parsers;

public class HttpProtocolParser : IProtocolParser
{
    public ProtocolType Type => ProtocolType.Http;
    public string Name => "HttpProtocolParser";
    public string Version => "1.0.0";
    public string Description => "HTTP raw payload parser";
    public string Vendor => "Vktun";
    public string[] SupportedDeviceModels => new[] { "*" };
    public string Author => "Vktun";
    public ParserStatus Status => ParserStatus.Stable;

    public HttpProtocolParser(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
    }

    public List<DeviceData> Parse(byte[] rawData, ProtocolConfig config)
    {
        return Parse(new ReadOnlySpan<byte>(rawData), config);
    }

    public List<DeviceData> Parse(ReadOnlySpan<byte> rawData, ProtocolConfig config)
    {
        return new List<DeviceData>
        {
            new()
            {
                ChannelId = config.ChannelId,
                ProtocolType = Type,
                CollectTime = DateTime.Now,
                RawData = rawData.ToArray(),
                IsValid = true
            }
        };
    }

    public byte[] Pack(DeviceData data, ProtocolConfig config)
    {
        return data.RawData ?? Array.Empty<byte>();
    }

    public byte[] Pack(DeviceCommand command, ProtocolConfig config)
    {
        if (command.Data is { Length: > 0 })
        {
            return command.Data;
        }

        if (command.Parameters.TryGetValue("Data", out var dataValue) && dataValue is byte[] data)
        {
            return data;
        }

        return Array.Empty<byte>();
    }

    public bool Validate(byte[] rawData, ProtocolConfig config)
    {
        return rawData.Length > 0;
    }
}
