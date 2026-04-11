using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Configuration.Providers;

public class JsonConfigurationProvider : IConfigurationProvider
{
    private readonly ILogger _logger;
    private readonly string _configFilePath;
    private readonly object _lockObject = new();
    private SdkConfig _config;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public event EventHandler<ConfigChangedEventArgs>? ConfigChanged;

    public JsonConfigurationProvider(ILogger logger, string configFilePath = "sdk_config.json")
    {
        _logger = logger;
        _configFilePath = configFilePath;
        _config = BuildDefaultConfig();
    }

    public SdkConfig GetConfig()
    {
        lock (_lockObject)
        {
            return JsonSerializer.Deserialize<SdkConfig>(JsonSerializer.Serialize(_config, JsonOptions), JsonOptions)
                ?? new SdkConfig();
        }
    }

    public async Task<SdkConfig> LoadConfigAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.Warning($"Configuration file does not exist: {filePath}. Using defaults.");
                return _config;
            }

            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            var config = JsonSerializer.Deserialize<SdkConfig>(json, JsonOptions);
            if (config == null)
            {
                return _config;
            }

            lock (_lockObject)
            {
                var oldConfig = _config;
                _config = config;
                ConfigChanged?.Invoke(this, new ConfigChangedEventArgs
                {
                    OldConfig = oldConfig,
                    NewConfig = config,
                    Timestamp = DateTime.Now
                });
            }

            _logger.Info($"Configuration loaded from {filePath}");
            return _config;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load configuration: {ex.Message}", ex);
            return _config;
        }
    }

    public async Task SaveConfigAsync(string filePath, SdkConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
            _logger.Info($"Configuration saved to {filePath}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to save configuration: {ex.Message}", ex);
        }
    }

    public Task<bool> UpdateConfigAsync(Action<SdkConfig> updateAction)
    {
        try
        {
            lock (_lockObject)
            {
                var oldConfig = JsonSerializer.Deserialize<SdkConfig>(JsonSerializer.Serialize(_config, JsonOptions), JsonOptions)
                    ?? new SdkConfig();

                updateAction(_config);
                ConfigChanged?.Invoke(this, new ConfigChangedEventArgs
                {
                    OldConfig = oldConfig,
                    NewConfig = _config,
                    Timestamp = DateTime.Now
                });
            }

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to update configuration: {ex.Message}", ex);
            return Task.FromResult(false);
        }
    }

    public Task<List<string>> GetProtocolTemplatePathsAsync(string templatesDirectory)
    {
        try
        {
            if (!Directory.Exists(templatesDirectory))
            {
                _logger.Warning($"Protocol template directory does not exist: {templatesDirectory}");
                return Task.FromResult(new List<string>());
            }

            return Task.FromResult(Directory.GetFiles(templatesDirectory, "*.json", SearchOption.AllDirectories).ToList());
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to enumerate protocol templates: {ex.Message}", ex);
            return Task.FromResult(new List<string>());
        }
    }

    public async Task<ProtocolConfig?> LoadProtocolTemplateAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.Warning($"Protocol template file does not exist: {filePath}");
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            var config = BuildProtocolConfig(json, filePath);
            if (config != null)
            {
                _logger.Info($"Protocol template loaded from {filePath}");
            }

            return config;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load protocol template {filePath}: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<List<ProtocolConfig>> LoadProtocolTemplatesAsync(string templatesDirectory)
    {
        var result = new List<ProtocolConfig>();
        var paths = await GetProtocolTemplatePathsAsync(templatesDirectory).ConfigureAwait(false);
        foreach (var path in paths)
        {
            var config = await LoadProtocolTemplateAsync(path).ConfigureAwait(false);
            if (config != null)
            {
                result.Add(config);
            }
        }

        return result;
    }

    public async Task SaveProtocolTemplateAsync(string filePath, ProtocolConfig config)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = BuildProtocolJson(config);
            await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
            _logger.Info($"Protocol template saved to {filePath}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to save protocol template {filePath}: {ex.Message}", ex);
        }
    }

    public async Task<bool> ExportTemplateAsync(ProtocolConfig config, string exportPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(exportPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var exportData = new ProtocolTemplateExport
            {
                ExportTime = DateTime.Now,
                ExportVersion = "1.0",
                Config = config
            };

            var json = JsonSerializer.Serialize(exportData, JsonOptions);
            await File.WriteAllTextAsync(exportPath, json).ConfigureAwait(false);
            _logger.Info($"Protocol template exported to {exportPath}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to export template: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<ProtocolConfig?> ImportTemplateAsync(string importPath)
    {
        try
        {
            if (!File.Exists(importPath))
            {
                _logger.Warning($"Import file does not exist: {importPath}");
                return null;
            }

            var json = await File.ReadAllTextAsync(importPath).ConfigureAwait(false);

            ProtocolConfig? config;
            var exportData = JsonSerializer.Deserialize<ProtocolTemplateExport>(json, JsonOptions);
            if (exportData?.Config != null)
            {
                config = exportData.Config;
            }
            else
            {
                config = BuildProtocolConfig(json, importPath);
            }

            if (config != null)
            {
                var validation = config.Validate();
                if (!validation.IsValid)
                {
                    _logger.Warning($"Imported template has validation errors: {string.Join(", ", validation.Errors)}");
                }

                config.TemplateSource = importPath;
                _logger.Info($"Protocol template imported from {importPath}");
            }

            return config;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to import template: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<ProtocolTemplateVersion?> GetTemplateVersionAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var fileInfo = new FileInfo(filePath);
            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new ProtocolTemplateVersion
            {
                FilePath = filePath,
                ConfigVersion = root.TryGetProperty("ConfigVersion", out var v) ? v.GetInt32() : 1,
                ProtocolVersion = root.TryGetProperty("ProtocolVersion", out var pv) && pv.ValueKind == JsonValueKind.String ? pv.GetString() ?? "1.0.0" : "1.0.0",
                LastModified = fileInfo.LastWriteTimeUtc,
                FileSize = fileInfo.Length
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get template version: {ex.Message}", ex);
            return null;
        }
    }

    public async Task StartTemplateWatchAsync(string templatesDirectory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(templatesDirectory))
        {
            _logger.Warning($"Template directory does not exist: {templatesDirectory}");
            return;
        }

        _logger.Info($"Starting template watch on {templatesDirectory}");

        using var watcher = new FileSystemWatcher(templatesDirectory)
        {
            Filter = "*.json",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        var changed = new SemaphoreSlim(0, 1);
        var changedFiles = new ConcurrentBag<string>();

        watcher.Changed += (_, e) => { changedFiles.Add(e.FullPath); changed.Release(); };
        watcher.Created += (_, e) => { changedFiles.Add(e.FullPath); changed.Release(); };
        watcher.Deleted += (_, e) => { changedFiles.Add(e.FullPath); changed.Release(); };
        watcher.Renamed += (_, e) => { changedFiles.Add(e.OldFullPath); changed.Release(); };

        watcher.EnableRaisingEvents = true;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await changed.WaitAsync(cancellationToken).ConfigureAwait(false);

                while (changedFiles.TryTake(out var filePath))
                {
                    _logger.Info($"Template file changed: {filePath}");
                    ConfigChanged?.Invoke(this, new ConfigChangedEventArgs
                    {
                        Timestamp = DateTime.Now,
                        AffectedFilePath = filePath
                    });
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        watcher.EnableRaisingEvents = false;
    }

    public ProtocolConfigValidationReport ValidateTemplate(ProtocolConfig config)
    {
        var result = config.Validate();
        return new ProtocolConfigValidationReport
        {
            ProtocolId = config.ProtocolId,
            ProtocolName = config.ProtocolName,
            ProtocolType = config.ProtocolType,
            IsValid = result.IsValid,
            Errors = result.Errors,
            Warnings = result.Warnings,
            ValidatedAt = DateTime.Now
        };
    }

    public async Task<List<ProtocolConfigValidationReport>> ValidateAllTemplatesAsync(string templatesDirectory)
    {
        var reports = new List<ProtocolConfigValidationReport>();
        var templates = await LoadProtocolTemplatesAsync(templatesDirectory).ConfigureAwait(false);

        foreach (var template in templates)
        {
            reports.Add(ValidateTemplate(template));
        }

        return reports;
    }

    private static string BuildProtocolJson(ProtocolConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.DefinitionJson))
        {
            var definition = JsonSerializer.Deserialize<object>(config.DefinitionJson, JsonOptions);
            return JsonSerializer.Serialize(definition, JsonOptions);
        }

        return JsonSerializer.Serialize(config, JsonOptions);
    }

    private static ProtocolConfig? BuildProtocolConfig(string json, string filePath)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var protocolType = ResolveProtocolType(root, filePath);
        if (protocolType == null)
        {
            return null;
        }

        var config = new ProtocolConfig
        {
            ProtocolId = ReadString(root, "ProtocolId") ?? Path.GetFileNameWithoutExtension(filePath),
            ProtocolName = ReadString(root, "ProtocolName") ?? Path.GetFileNameWithoutExtension(filePath),
            Description = ReadString(root, "Description") ?? string.Empty,
            ProtocolType = protocolType.Value,
            ProtocolVersion = ReadString(root, "ProtocolVersion") ?? "1.0.0",
            Vendor = ReadString(root, "Vendor") ?? string.Empty,
            DeviceModel = ReadString(root, "DeviceModel") ?? string.Empty,
            TemplateSource = filePath,
            DefinitionJson = json
        };

        ApplyLegacyParseRules(config);
        return config;
    }

    private static void ApplyLegacyParseRules(ProtocolConfig config)
    {
        switch (config.ProtocolType)
        {
            case ProtocolType.Custom:
                config.ParseRules["CustomProtocolJson"] = config.DefinitionJson;
                break;
            case ProtocolType.ModbusRtu:
            case ProtocolType.ModbusTcp:
                config.ParseRules["ModbusConfig"] = config.DefinitionJson;
                break;
            case ProtocolType.S7:
                ApplyS7ParseRules(config);
                break;
            case ProtocolType.IEC104:
                ApplyIec104ParseRules(config);
                break;
        }
    }

    private static void ApplyS7ParseRules(ProtocolConfig config)
    {
        var definition = config.GetDefinition<S7Config>();
        if (definition == null)
        {
            return;
        }

        config.ParseRules["CpuType"] = definition.CpuType.ToString();
        config.ParseRules["Rack"] = definition.Rack.ToString();
        config.ParseRules["Slot"] = definition.Slot.ToString();
        config.ParseRules["Port"] = definition.Port.ToString();
        config.ParseRules["PduSize"] = definition.PduSize.ToString();
        config.ParseRules["Points"] = JsonSerializer.Serialize(definition.Points, JsonOptions);
    }

    private static void ApplyIec104ParseRules(ProtocolConfig config)
    {
        var definition = config.GetDefinition<IEC104Config>();
        if (definition == null)
        {
            return;
        }

        config.ParseRules["CommonAddress"] = definition.CommonAddress.ToString();
        config.ParseRules["Port"] = definition.Port.ToString();
        config.ParseRules["Points"] = JsonSerializer.Serialize(definition.Points, JsonOptions);
    }

    private static ProtocolType? ResolveProtocolType(JsonElement root, string filePath)
    {
        if (TryReadString(root, "ProtocolType", out var protocolTypeText) &&
            Enum.TryParse<ProtocolType>(protocolTypeText, true, out var protocolType))
        {
            return protocolType;
        }

        if (TryReadString(root, "ModbusType", out var modbusType))
        {
            return modbusType.Equals("Tcp", StringComparison.OrdinalIgnoreCase)
                ? ProtocolType.ModbusTcp
                : ProtocolType.ModbusRtu;
        }

        if (root.TryGetProperty("FrameType", out _))
        {
            return ProtocolType.Custom;
        }

        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (fileName.Contains("S7", StringComparison.OrdinalIgnoreCase))
        {
            return ProtocolType.S7;
        }

        if (fileName.Contains("104", StringComparison.OrdinalIgnoreCase))
        {
            return ProtocolType.IEC104;
        }

        return null;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return TryReadString(root, propertyName, out var value) ? value : null;
    }

    private static bool TryReadString(JsonElement root, string propertyName, out string value)
    {
        if (root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static SdkConfig BuildDefaultConfig()
    {
        return new SdkConfig
        {
            Global = new GlobalConfig
            {
                MaxConcurrentConnections = 1000,
                BufferSize = 8192,
                ConnectionTimeout = 5000,
                MaxReconnectCount = 100,
                ReconnectBaseInterval = 1000,
                ReconnectMaxInterval = 30000,
                EnableDataCache = true,
                CacheMaxSize = 10000
            },
            Tcp = new TcpConfig
            {
                MaxServerConnections = 1000,
                HeartbeatInterval = 15000,
                HeartbeatTimeout = 30000,
                NoDelay = true,
                SessionIdleTimeout = 3600000,
                ListenBacklog = 100,
                ReceiveBufferSize = 8192,
                SendBufferSize = 8192
            },
            Udp = new UdpConfig
            {
                MaxOnlineDevices = 5000,
                HeartbeatCheckInterval = 20000,
                DeviceOfflineTimeout = 40000,
                ReceiveBufferSize = 65536,
                MaxDataRate = 1000
            },
            Serial = new SerialConfig
            {
                MaxDevicesPerPort = 32,
                PollingInterval = 100,
                ReceivePollingInterval = 10,
                ReadWriteTimeout = 500,
                MaxConcurrentPorts = 4
            },
            Wireless = new WirelessConfig
            {
                HeartbeatInterval = 15000,
                OfflineTimeout = 40000,
                AtCommandTimeout = 2000,
                NbHeartbeatInterval = 30000,
                DataCacheSize = 2000,
                MaxDevicesPerChannel = 500
            },
            ThreadPool = new ThreadPoolConfig
            {
                MinWorkerThreads = 10,
                MaxWorkerThreads = 100,
                MinCompletionPortThreads = 10,
                MaxCompletionPortThreads = 100,
                TaskQueueCapacity = 10000
            },
            Resource = new ResourceConfig
            {
                MaxCpuUsage = 80,
                MaxMemoryUsage = 1024 * 1024 * 1024,
                MaxSocketHandles = 10000,
                MonitorInterval = 5000,
                EnableResourceMonitor = true
            }
        };
    }
}
