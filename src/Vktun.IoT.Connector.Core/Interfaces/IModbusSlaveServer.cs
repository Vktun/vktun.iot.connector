using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Core.Interfaces;

/// <summary>
/// Public SDK contract for a local Modbus TCP slave/server simulator.
/// </summary>
public interface IModbusSlaveServer : IAsyncDisposable
{
    /// <summary>
    /// Raised whenever the server receives a Modbus TCP request and either sends a response or rejects it.
    /// </summary>
    event EventHandler<ModbusSlaveTrafficEntry>? TrafficReceived;

    /// <summary>
    /// Gets a value indicating whether the server is currently listening.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets the options used by the active server, when started.
    /// </summary>
    ModbusSlaveOptions? Options { get; }

    /// <summary>
    /// Gets the active Modbus data store.
    /// </summary>
    ModbusSlaveDataStore DataStore { get; }

    /// <summary>
    /// Starts the Modbus TCP slave server.
    /// </summary>
    /// <param name="options">Server listen options.</param>
    /// <param name="dataStore">Optional pre-seeded data store. When omitted, a store is created from the counts in <paramref name="options"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    Task<ModbusSlaveOperationResult> StartAsync(
        ModbusSlaveOptions options,
        ModbusSlaveDataStore? dataStore = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the Modbus TCP slave server and closes active clients.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    Task<ModbusSlaveOperationResult> StopAsync(CancellationToken cancellationToken = default);
}
