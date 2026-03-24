using System.Collections.Concurrent;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Protocol.Parsers;

namespace Vktun.IoT.Connector.Protocol.Factories;

public class ProtocolParserFactory : IProtocolParserFactory
{
    private readonly ConcurrentDictionary<ProtocolType, IProtocolParser> _parsers;
    private readonly ConcurrentDictionary<string, IProtocolParser> _protocolParsers;
    private readonly ILogger _logger;

    public ProtocolParserFactory(ILogger logger)
    {
        _parsers = new ConcurrentDictionary<ProtocolType, IProtocolParser>();
        _protocolParsers = new ConcurrentDictionary<string, IProtocolParser>();
        _logger = logger;
        
        RegisterDefaultParsers();
    }

    private void RegisterDefaultParsers()
    {
        RegisterParser(new CustomProtocolParser(_logger));
        RegisterParser(new ModbusRtuParser(_logger));
        RegisterParser(new ModbusTcpParser(_logger));
        RegisterParser(new S7ProtocolParser(_logger));
        RegisterParser(new IEC104ProtocolParser(_logger));
    }

    public IProtocolParser? GetParser(ProtocolType type)
    {
        return _parsers.TryGetValue(type, out var parser) ? parser : null;
    }

    public IProtocolParser? GetParser(string protocolId)
    {
        return _protocolParsers.TryGetValue(protocolId, out var parser) ? parser : null;
    }

    public void RegisterParser(IProtocolParser parser)
    {
        _parsers[parser.Type] = parser;
        _logger.Info($"注册协议解析器: {parser.Name}, 类型: {parser.Type}");
    }

    public void UnregisterParser(ProtocolType type)
    {
        _parsers.TryRemove(type, out _);
        _logger.Info($"注销协议解析器: {type}");
    }

    public IEnumerable<ProtocolType> GetSupportedProtocols()
    {
        return _parsers.Keys;
    }
}
