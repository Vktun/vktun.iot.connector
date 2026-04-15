using Microsoft.Extensions.DependencyInjection.Extensions;
using Vktun.IoT.Connector;
using Vktun.IoT.Connector.Business.Factories;
using Vktun.IoT.Connector.Business.Managers;
using Vktun.IoT.Connector.Business.Providers;
using Vktun.IoT.Connector.Business.Services;
using Vktun.IoT.Connector.Communication.Channels;
using Vktun.IoT.Connector.Communication.Mqtt;
using Vktun.IoT.Connector.Concurrency.Monitors;
using Vktun.IoT.Connector.Configuration.Logging;
using Vktun.IoT.Connector.Configuration.Providers;
using Vktun.IoT.Connector.DependencyInjection;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Protocol.Factories;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Dependency injection extensions for Vktun IoT Connector.
/// </summary>
public static class VktunIoTConnectorServiceCollectionExtensions
{
    /// <summary>
    /// Adds the default Vktun IoT Connector runtime services and the <see cref="IIoTDataCollector"/> facade.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configure">Optional registration options.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddVktunIoTConnector(
        this IServiceCollection services,
        Action<VktunIoTConnectorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = CreateOptions(configure);
        services.AddVktunCoreInfrastructure(options);
        services.AddHttpClient();

        services.TryAddSingleton<IProtocolParserFactory, ProtocolParserFactory>();
        services.TryAddSingleton<ICommunicationChannelFactory, CommunicationChannelFactory>();
        services.TryAddSingleton<IDeviceCommandExecutor, DeviceCommandExecutor>();
        services.TryAddSingleton<ISessionManager, SessionManager>();
        services.TryAddSingleton<IDeviceManager>(serviceProvider =>
        {
            var config = serviceProvider.GetRequiredService<IConfigurationProvider>().GetConfig().Global;
            return new DeviceManager(
                serviceProvider.GetRequiredService<ISessionManager>(),
                serviceProvider.GetRequiredService<IDeviceCommandExecutor>(),
                serviceProvider.GetRequiredService<ILogger>(),
                config.MaxReconnectCount,
                config.ReconnectBaseInterval,
                config.ReconnectMaxInterval,
                serviceProvider.GetRequiredService<IResourceMonitor>());
        });
        services.TryAddSingleton<Vktun.IoT.Connector.Core.Interfaces.ITaskScheduler>(serviceProvider =>
        {
            return new Vktun.IoT.Connector.Concurrency.Schedulers.TaskScheduler(
                serviceProvider.GetRequiredService<IConfigurationProvider>(),
                serviceProvider.GetRequiredService<IDeviceManager>(),
                serviceProvider.GetRequiredService<IDeviceCommandExecutor>(),
                serviceProvider.GetRequiredService<ILogger>());
        });
        services.TryAddSingleton<IResourceMonitor, ResourceMonitor>();
        services.TryAddSingleton<IHeartbeatManager, HeartbeatManager>();
        services.TryAddSingleton<IDataCache>(serviceProvider =>
        {
            var config = serviceProvider.GetRequiredService<IConfigurationProvider>().GetConfig();
            var logger = serviceProvider.GetRequiredService<ILogger>();
            return CreateDataCache(config, logger);
        });
        services.TryAddSingleton<IPersistentDataStore>(serviceProvider =>
        {
            var config = serviceProvider.GetRequiredService<IConfigurationProvider>().GetConfig().Persistence;
            var logger = serviceProvider.GetRequiredService<ILogger>();
            var store = CreatePersistentDataStore(serviceProvider, options, config);
            return config.EnableWriteBuffer
                ? new BufferedDataPersistenceStore(store, config, logger)
                : store;
        });
        services.TryAddSingleton<IDataProvider, DataProvider>();
        services.TryAddSingleton<IIoTDataCollector, IoTDataCollector>();
        services.TryAddSingleton(serviceProvider => (IoTDataCollector)serviceProvider.GetRequiredService<IIoTDataCollector>());

        return services;
    }

    /// <summary>
    /// Adds the default infrastructure required for HTTP channels.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configure">Optional registration options.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddVktunHttpChannel(
        this IServiceCollection services,
        Action<VktunIoTConnectorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = CreateOptions(configure);
        services.AddVktunCoreInfrastructure(options);
        services.AddHttpClient();
        services.TryAddSingleton<ICommunicationChannelFactory, CommunicationChannelFactory>();
        services.TryAddTransient<HttpClientChannel>();

        return services;
    }

    /// <summary>
    /// Adds the default infrastructure required for MQTT channels and the high-level MQTT messaging client.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configureMqtt">Optional MQTT client options.</param>
    /// <param name="configure">Optional registration options.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddVktunMqttChannel(
        this IServiceCollection services,
        Action<MqttConfig>? configureMqtt = null,
        Action<VktunIoTConnectorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = CreateOptions(configure);
        var mqttConfig = new MqttConfig();
        configureMqtt?.Invoke(mqttConfig);

        services.AddVktunCoreInfrastructure(options);
        services.TryAddSingleton(mqttConfig);
        services.TryAddSingleton<ICommunicationChannelFactory, CommunicationChannelFactory>();
        services.TryAddTransient(serviceProvider => new MqttChannel(
            serviceProvider.GetRequiredService<IConfigurationProvider>(),
            serviceProvider.GetRequiredService<ILogger>(),
            serviceProvider.GetRequiredService<MqttConfig>()));
        services.TryAddTransient<IMqttMessagingClient>(serviceProvider => new MqttMessagingClient(
            serviceProvider.GetRequiredService<IConfigurationProvider>(),
            serviceProvider.GetRequiredService<ILogger>(),
            serviceProvider.GetRequiredService<MqttConfig>()));

        return services;
    }

    private static VktunIoTConnectorOptions CreateOptions(
        Action<VktunIoTConnectorOptions>? configure)
    {
        var options = new VktunIoTConnectorOptions();
        configure?.Invoke(options);
        return options;
    }

    private static IServiceCollection AddVktunCoreInfrastructure(
        this IServiceCollection services,
        VktunIoTConnectorOptions options)
    {
        services.TryAddSingleton<ILogger>(_ => new ConsoleLogger(options.MinimumLogLevel));
        services.TryAddSingleton<IConfigurationProvider>(serviceProvider =>
        {
            var provider = new JsonConfigurationProvider(
                serviceProvider.GetRequiredService<ILogger>(),
                options.ConfigFilePath);

            if (options.ConfigureSdk != null)
            {
                provider.UpdateConfigAsync(options.ConfigureSdk).GetAwaiter().GetResult();
            }

            return provider;
        });

        return services;
    }

    private static IDataCache CreateDataCache(SdkConfig config, ILogger logger)
    {
        var cacheConfig = config.Cache ?? new CacheConfig();
        var maxSize = cacheConfig.MaxSize > 0 ? cacheConfig.MaxSize : config.Global.CacheMaxSize;

        if (cacheConfig.IsUseRedis &&
            !string.IsNullOrWhiteSpace(cacheConfig.Redis.ConnectionString))
        {
            try
            {
                var redisConfig = new CacheConfig
                {
                    IsUseRedis = true,
                    Backend = DataCacheBackend.Redis,
                    MaxSize = maxSize,
                    Redis = cacheConfig.Redis
                };

                var redisCache = RedisDataCache.TryCreate(redisConfig);
                if (redisCache != null)
                {
                    return redisCache;
                }
            }
            catch (Exception ex)
            {
                logger.Warning($"Redis cache is not available. Falling back to in-memory cache. {ex.Message}");
            }
        }

        return new DataCache(maxSize);
    }

    private static IPersistentDataStore CreatePersistentDataStore(
        IServiceProvider serviceProvider,
        VktunIoTConnectorOptions options,
        DataPersistenceConfig config)
    {
        if (!config.Enabled || config.Backend == DataPersistenceBackend.None)
        {
            return new NoopDataPersistenceStore();
        }

        return config.Backend switch
        {
            DataPersistenceBackend.Memory => new MemoryDataPersistenceStore(config.MaxHistoryItems),
            DataPersistenceBackend.File => new FileDataPersistenceStore(config.FilePath),
            DataPersistenceBackend.Sqlite => new SqliteDataPersistenceStore(config.SqliteConnectionString),
            DataPersistenceBackend.External when options.ExternalDataStoreFactory != null => options.ExternalDataStoreFactory(serviceProvider),
            DataPersistenceBackend.External => new NoopDataPersistenceStore(),
            _ => new NoopDataPersistenceStore()
        };
    }
}
