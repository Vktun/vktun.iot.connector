using System.Collections.Concurrent;
using System.Reflection;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Protocol.Parsers;

namespace Vktun.IoT.Connector.Protocol.Factories;

public class ProtocolParserFactory : IProtocolParserFactory
{
    private readonly ConcurrentDictionary<ProtocolType, IProtocolParser> _parsers = new();
    private readonly ConcurrentDictionary<string, IProtocolParser> _protocolParsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ParserDescriptor> _descriptors = new(StringComparer.OrdinalIgnoreCase);
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

    public IProtocolParser? GetParser(ProtocolType type, string version, string deviceModel)
    {
        var byType = GetParser(type);
        if (byType == null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(version) && string.IsNullOrWhiteSpace(deviceModel))
        {
            return byType;
        }

        var descriptor = GetDescriptor(byType);
        if (descriptor == null)
        {
            return byType;
        }

        if (!string.IsNullOrWhiteSpace(version) && !string.IsNullOrWhiteSpace(descriptor.Version))
        {
            if (!IsVersionCompatible(descriptor.Version, version))
            {
                _logger.Warning($"Parser {byType.Name} version {descriptor.Version} is not compatible with requested version {version}.");
                return null;
            }
        }

        if (!string.IsNullOrWhiteSpace(deviceModel) && descriptor.SupportedDeviceModels.Length > 0)
        {
            if (!descriptor.SupportedDeviceModels.Any(m => m.Equals(deviceModel, StringComparison.OrdinalIgnoreCase) || m == "*"))
            {
                _logger.Warning($"Parser {byType.Name} does not support device model {deviceModel}.");
                return null;
            }
        }

        return byType;
    }

    public void RegisterParser(IProtocolParser parser)
    {
        RegisterTypeAlias(parser);
        RegisterNamedAlias(parser.Name, parser);

        _logger.Info($"Registered protocol parser {parser.Name}, type: {parser.Type}, version: {parser.Version}");
    }

    public void RegisterParser(string protocolId, IProtocolParser parser)
    {
        RegisterParser(parser);
        _protocolParsers[protocolId] = parser;
        _descriptors[protocolId] = new ParserDescriptor
        {
            ProtocolId = protocolId,
            ProtocolType = parser.Type,
            Name = parser.Name,
            Version = parser.Version,
            Description = parser.Description,
            Vendor = parser.Vendor,
            SupportedDeviceModels = parser.SupportedDeviceModels,
            Author = parser.Author,
            Status = parser.Status
        };
    }

    public void UnregisterParser(ProtocolType type)
    {
        _parsers.TryRemove(type, out _);
        _protocolParsers.TryRemove(type.ToString(), out _);
        _descriptors.TryRemove(type.ToString(), out _);
        _logger.Info($"Unregistered protocol parser {type}");
    }

    public void UnregisterParser(string protocolId)
    {
        _protocolParsers.TryRemove(protocolId, out _);
        _descriptors.TryRemove(protocolId, out _);
        _logger.Info($"Unregistered protocol parser {protocolId}");
    }

    public IEnumerable<ProtocolType> GetSupportedProtocols()
    {
        return _parsers.Keys;
    }

    public IEnumerable<ParserDescriptor> GetAllParserDescriptors()
    {
        return _descriptors.Values.DistinctBy(d => $"{d.ProtocolType}|{d.Name}|{d.Version}");
    }

    public void LoadPluginParsers(string pluginDirectory)
    {
        if (!Directory.Exists(pluginDirectory))
        {
            _logger.Warning($"Plugin directory does not exist: {pluginDirectory}");
            return;
        }

        var dllFiles = Directory.GetFiles(pluginDirectory, "Vktun.IoT.Connector.Protocol.*.dll", SearchOption.TopDirectoryOnly);
        foreach (var dllPath in dllFiles)
        {
            try
            {
                var assembly = Assembly.LoadFrom(dllPath);
                var parserTypes = assembly.GetTypes()
                    .Where(t => typeof(IProtocolParser).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

                foreach (var parserType in parserTypes)
                {
                    try
                    {
                        if (Activator.CreateInstance(parserType, _logger) is IProtocolParser parser)
                        {
                            RegisterParser(parser);
                            _logger.Info($"Loaded plugin parser: {parser.Name} from {Path.GetFileName(dllPath)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Failed to instantiate plugin parser {parserType.Name}: {ex.Message}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load plugin assembly {dllPath}: {ex.Message}", ex);
            }
        }
    }

    private ParserDescriptor? GetDescriptor(IProtocolParser parser)
    {
        if (_descriptors.TryGetValue(parser.Name, out var descriptor))
        {
            return descriptor;
        }

        if (_descriptors.TryGetValue(parser.Type.ToString(), out descriptor))
        {
            return descriptor;
        }

        return null;
    }

    private void RegisterTypeAlias(IProtocolParser parser)
    {
        var protocolKey = parser.Type.ToString();

        if (_parsers.TryGetValue(parser.Type, out var existing))
        {
            if (!ShouldReplaceDefaultParser(existing, parser))
            {
                _logger.Info($"Preserving default parser {existing.Name} for type {parser.Type}; {parser.Name} remains addressable by explicit alias.");
                return;
            }

            _logger.Warning($"Replacing default parser {existing.Name} with {parser.Name} for type {parser.Type}.");
        }

        _parsers[parser.Type] = parser;
        _protocolParsers[protocolKey] = parser;
        _descriptors[protocolKey] = CreateDescriptor(protocolKey, parser);
    }

    private void RegisterNamedAlias(string alias, IProtocolParser parser)
    {
        _protocolParsers[alias] = parser;
        _descriptors[alias] = CreateDescriptor(alias, parser);
    }

    private static ParserDescriptor CreateDescriptor(string protocolId, IProtocolParser parser)
    {
        return new ParserDescriptor
        {
            ProtocolId = protocolId,
            ProtocolType = parser.Type,
            Name = parser.Name,
            Version = parser.Version,
            Description = parser.Description,
            Vendor = parser.Vendor,
            SupportedDeviceModels = parser.SupportedDeviceModels,
            Author = parser.Author,
            Status = parser.Status
        };
    }

    private static bool ShouldReplaceDefaultParser(IProtocolParser existing, IProtocolParser candidate)
    {
        var existingPriority = GetStatusPriority(existing.Status);
        var candidatePriority = GetStatusPriority(candidate.Status);

        return candidatePriority > existingPriority;
    }

    private static int GetStatusPriority(ParserStatus status)
    {
        return status switch
        {
            ParserStatus.Stable => 3,
            ParserStatus.Experimental => 2,
            ParserStatus.Deprecated => 1,
            _ => 0
        };
    }

    private static bool IsVersionCompatible(string parserVersion, string requestedVersion)
    {
        if (parserVersion.Equals(requestedVersion, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parserParts = parserVersion.Split('.');
        var requestedParts = requestedVersion.Split('.');

        if (parserParts.Length > 0 && requestedParts.Length > 0)
        {
            return parserParts[0].Equals(requestedParts[0], StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private void RegisterDefaultParsers()
    {
        RegisterParser(new CustomProtocolParser(_logger));
        RegisterParser(new ModbusRtuParser(_logger));
        RegisterParser(new ModbusTcpParser(_logger));
        RegisterParser(new HttpProtocolParser(_logger));
        RegisterParser(new S7ProtocolParser(_logger));
        RegisterParser(new IEC104ProtocolParser(_logger));
        RegisterParser(new BacnetProtocolParser(_logger));
        RegisterParser(new OpcUaProtocolParser(_logger));
        RegisterParser(new CanopenProtocolParser(_logger));
    }
}
