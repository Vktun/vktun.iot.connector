using Microsoft.Extensions.DependencyInjection;
using Vktun.IoT.Connector.Business.Providers;
using Vktun.IoT.Connector.Communication.Channels;
using Vktun.IoT.Connector.Communication.Mqtt;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.DependencyInjection;

public class VktunIoTConnectorServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddVktunIoTConnector_ShouldResolveCollectorAndRuntimeServices()
    {
        var services = new ServiceCollection();

        services.AddVktunIoTConnector(options =>
        {
            options.ConfigureSdk = config =>
            {
                config.Global.CacheMaxSize = 128;
                config.Global.MaxReconnectCount = 2;
            };
        });

        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IIoTDataCollector>());
        Assert.NotNull(provider.GetRequiredService<ICommunicationChannelFactory>());
        Assert.NotNull(provider.GetRequiredService<IProtocolParserFactory>());
        Assert.NotNull(provider.GetRequiredService<IDeviceCommandExecutor>());
        Assert.NotNull(provider.GetRequiredService<IDeviceManager>());
        Assert.NotNull(provider.GetRequiredService<Vktun.IoT.Connector.Core.Interfaces.ITaskScheduler>());
        Assert.IsType<DataCache>(provider.GetRequiredService<IDataCache>());
        Assert.Equal(128, provider.GetRequiredService<IDataCache>().MaxSize);
    }

    [Fact]
    public async Task AddVktunIoTConnector_WithUseRedisButNoConnectionString_ShouldUseMemoryDataCache()
    {
        var services = new ServiceCollection();

        services.AddVktunIoTConnector(options =>
        {
            options.ConfigureSdk = config =>
            {
                config.Cache.IsUseRedis = true;
                config.Cache.Backend = DataCacheBackend.Redis;
                config.Cache.MaxSize = 64;
                config.Cache.Redis.ConnectionString = string.Empty;
            };
        });

        await using var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredService<IDataCache>();
        Assert.IsType<DataCache>(cache);
        Assert.Equal(64, cache.MaxSize);
    }

    [Fact]
    public async Task AddVktunIoTConnector_WithRedisConfigButUseRedisFalse_ShouldUseMemoryDataCache()
    {
        var services = new ServiceCollection();

        services.AddVktunIoTConnector(options =>
        {
            options.ConfigureSdk = config =>
            {
                config.Cache.IsUseRedis = false;
                config.Cache.Backend = DataCacheBackend.Redis;
                config.Cache.MaxSize = 32;
                config.Cache.Redis.ConnectionString = "localhost:6379";
            };
        });

        await using var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredService<IDataCache>();
        Assert.IsType<DataCache>(cache);
        Assert.Equal(32, cache.MaxSize);
    }

    [Fact]
    public async Task AddVktunHttpChannel_ShouldResolveHttpChannelAndFactory()
    {
        var services = new ServiceCollection();

        services.AddVktunHttpChannel(options =>
        {
            options.ConfigureSdk = config => config.Http.MaxConnectionsPerServer = 32;
        });

        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<HttpClientChannel>());
        Assert.NotNull(provider.GetRequiredService<ICommunicationChannelFactory>());
        Assert.Equal(32, provider.GetRequiredService<IConfigurationProvider>().GetConfig().Http.MaxConnectionsPerServer);
    }

    [Fact]
    public async Task AddVktunMqttChannel_ShouldResolveMqttClientWithConfiguredDefaults()
    {
        var services = new ServiceCollection();

        services.AddVktunMqttChannel(mqtt =>
        {
            mqtt.Server = "broker.example.test";
            mqtt.ClientId = "di-test-client";
            mqtt.UseInMemoryTransport = true;
        });

        await using var provider = services.BuildServiceProvider();

        var mqttConfig = provider.GetRequiredService<MqttConfig>();
        Assert.Equal("broker.example.test", mqttConfig.Server);
        Assert.Equal("di-test-client", mqttConfig.ClientId);
        Assert.True(mqttConfig.UseInMemoryTransport);
        Assert.NotNull(provider.GetRequiredService<MqttChannel>());
        Assert.NotNull(provider.GetRequiredService<IMqttMessagingClient>());
    }
}
