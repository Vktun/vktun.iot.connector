using System.Collections.Concurrent;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Protocol.Parsers;

namespace Vktun.IoT.Connector.Protocol.Factories;

public class ProtocolParserFactory : IProtocolParserFactory
{
    private readonly ConcurrentDictionary<ProtocolType, IProtocolParser> _parsers = new();
    private readonly ConcurrentDictionary<string, IProtocolParser> _protocolParsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _logger;

    public ProtocolParserFactory(ILogger logger)
    {
        _logger = logger;
        RegisterDefaultParsers();
    }

    public IProtocolParser? GetParser(ProtocolType type)
    {
        return _parsers.TryGetValue(type, out var parser) ? parser : null;
    }

    public IProtocolParser? GetParser(string protocolId)
    {
        if (_protocolParsers.TryGetValue(protocolId, out var parser))
        {
            return parser;
        }

        return Enum.TryParse<ProtocolType>(protocolId, true, out var protocolType)
            ? GetParser(protocolType)
            : null;
    }

    public void RegisterParser(IProtocolParser parser)
    {
        _parsers[parser.Type] = parser;
        _protocolParsers[parser.Type.ToString()] = parser;
        _protocolParsers[parser.Name] = parser;
        _logger.Info($"Registered protocol parser {parser.Name}, type: {parser.Type}");
    }

    public void RegisterParser(string protocolId, IProtocolParser parser)
    {
        RegisterParser(parser);
        _protocolParsers[protocolId] = parser;
    }

    public void UnregisterParser(ProtocolType type)
    {
        _parsers.TryRemove(type, out _);
        _protocolParsers.TryRemove(type.ToString(), out _);
        _logger.Info($"Unregistered protocol parser {type}");
    }

    public void UnregisterParser(string protocolId)
    {
        _protocolParsers.TryRemove(protocolId, out _);
        _logger.Info($"Unregistered protocol parser {protocolId}");
    }

    public IEnumerable<ProtocolType> GetSupportedProtocols()
    {
        return _parsers.Keys;
    }

    private void RegisterDefaultParsers()
    {
        RegisterParser(new CustomProtocolParser(_logger));
        RegisterParser(new ModbusRtuParser(_logger));
        RegisterParser(new ModbusTcpParser(_logger));
        RegisterParser(new S7ProtocolParser(_logger));
        RegisterParser(new IEC104ProtocolParser(_logger));
        RegisterParser(new BacnetProtocolParser(_logger));
        RegisterParser(new OpcUaProtocolParser(_logger));
        RegisterParser(new CanopenProtocolParser(_logger));
    }
}
