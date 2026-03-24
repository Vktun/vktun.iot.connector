using System.Text.Json;
using System.Text.Json.Serialization;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Configuration.Providers;

public class JsonConfigurationProvider : IConfigurationProvider
{
    private readonly ILogger _logger;
    private readonly string _configFilePath;
    private SdkConfig _config;
    private readonly object _lockObject = new();

    private static readonly JsonSerializerOptions _jsonOptions = new()
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
        _config = new SdkConfig();
        
        LoadDefaultConfig();
    }

    public SdkConfig GetConfig()
    {
        lock (_lockObject)
        {
            return _config;
        }
    }

    public async Task<SdkConfig> LoadConfigAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.Warning($"配置文件不存�? {filePath}, 使用默认配置");
                return _config;
            }

            var json = await File.ReadAllTextAsync(filePath);
            var config = JsonSerializer.Deserialize<SdkConfig>(json, _jsonOptions);
            
            if (config != null)
            {
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
                
                _logger.Info($"配置加载成功: {filePath}");
            }
            
            return _config;
        }
        catch (Exception ex)
        {
            _logger.Error($"配置加载失败: {ex.Message}", ex);
            return _config;
        }
    }

    public async Task SaveConfigAsync(string filePath, SdkConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
            
            _logger.Info($"配置保存成功: {filePath}");
        }
        catch (Exception ex)
        {
            _logger.Error($"配置保存失败: {ex.Message}", ex);
        }
    }

    public Task<bool> UpdateConfigAsync(Action<SdkConfig> updateAction)
    {
        try
        {
            lock (_lockObject)
            {
                var oldConfig = JsonSerializer.Deserialize<SdkConfig>(
                    JsonSerializer.Serialize(_config, _jsonOptions), _jsonOptions);
                
                updateAction(_config);
                
                ConfigChanged?.Invoke(this, new ConfigChangedEventArgs
                {
                    OldConfig = oldConfig ?? new SdkConfig(),
                    NewConfig = _config,
                    Timestamp = DateTime.Now
                });
            }
            
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.Error($"配置更新失败: {ex.Message}", ex);
            return Task.FromResult(false);
        }
    }

    public Task<List<string>> GetProtocolTemplatePathsAsync(string templatesDirectory)
    {
        try
        {
            if (!Directory.Exists(templatesDirectory))
            {
                _logger.Warning($"协议模板目录不存在: {templatesDirectory}");
                return Task.FromResult(new List<string>());
            }

            var jsonFiles = Directory.GetFiles(templatesDirectory, "*.json", SearchOption.AllDirectories).ToList();
            _logger.Info($"发现 {jsonFiles.Count} 个协议模板文件");
            return Task.FromResult(jsonFiles);
        }
        catch (Exception ex)
        {
            _logger.Error($"获取协议模板路径失败: {ex.Message}", ex);
            return Task.FromResult(new List<string>());
        }
    }

    public async Task<ProtocolConfig?> LoadProtocolTemplateAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.Warning($"协议模板文件不存在: {filePath}");
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath);
            var config = JsonSerializer.Deserialize<ProtocolConfig>(json, _jsonOptions);
            
            if (config != null)
            {
                _logger.Info($"协议模板加载成功: {filePath}");
            }
            
            return config;
        }
        catch (Exception ex)
        {
            _logger.Error($"协议模板加载失败: {filePath}, 错误: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<List<ProtocolConfig>> LoadProtocolTemplatesAsync(string templatesDirectory)
    {
        var result = new List<ProtocolConfig>();
        
        try
        {
            var templatePaths = await GetProtocolTemplatePathsAsync(templatesDirectory);
            
            foreach (var path in templatePaths)
            {
                var config = await LoadProtocolTemplateAsync(path);
                if (config != null)
                {
                    result.Add(config);
                }
            }
            
            _logger.Info($"成功加载 {result.Count} 个协议模板");
        }
        catch (Exception ex)
        {
            _logger.Error($"批量加载协议模板失败: {ex.Message}", ex);
        }
        
        return result;
    }

    public async Task SaveProtocolTemplateAsync(string filePath, ProtocolConfig config)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(config, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
            
            _logger.Info($"协议模板保存成功: {filePath}");
        }
        catch (Exception ex)
        {
            _logger.Error($"协议模板保存失败: {filePath}, 错误: {ex.Message}", ex);
        }
    }

    private void LoadDefaultConfig()
    {
        _config = new SdkConfig
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
