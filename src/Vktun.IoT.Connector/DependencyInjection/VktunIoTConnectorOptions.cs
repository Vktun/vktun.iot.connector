using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.DependencyInjection;

/// <summary>
/// Options used by the default dependency injection registrations for Vktun IoT Connector.
/// </summary>
public sealed class VktunIoTConnectorOptions
{
    /// <summary>
    /// Gets or sets the default JSON configuration file path used by the built-in configuration provider.
    /// </summary>
    public string ConfigFilePath { get; set; } = "sdk_config.json";

    /// <summary>
    /// Gets or sets the minimum log level for the built-in console logger.
    /// </summary>
    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Info;

    /// <summary>
    /// Gets or sets an action that can override the default SDK configuration at registration time.
    /// </summary>
    public Action<SdkConfig>? ConfigureSdk { get; set; }

    /// <summary>
    /// Gets or sets a factory for an externally managed persistent store.
    /// </summary>
    public Func<IServiceProvider, IPersistentDataStore>? ExternalDataStoreFactory { get; set; }
}
