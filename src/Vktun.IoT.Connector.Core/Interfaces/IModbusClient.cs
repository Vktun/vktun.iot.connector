using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Core.Interfaces;

/// <summary>
/// High-level Modbus master client for common TCP and RTU read/write workflows.
/// </summary>
public interface IModbusClient
{
    /// <summary>
    /// Opens a Modbus connection.
    /// </summary>
    /// <param name="options">Connection options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Connection result.</returns>
    Task<ModbusOperationResult> ConnectAsync(ModbusConnectionOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes a Modbus connection.
    /// </summary>
    /// <param name="connectionId">Connection identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Disconnect result.</returns>
    Task<ModbusOperationResult> DisconnectAsync(string connectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads coils, discrete inputs, input registers, or holding registers.
    /// </summary>
    /// <param name="request">Read request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Read result.</returns>
    Task<ModbusReadResult> ReadAsync(ModbusReadRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes coils or holding registers.
    /// </summary>
    /// <param name="request">Write request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Write result.</returns>
    Task<ModbusOperationResult> WriteAsync(ModbusWriteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Repeatedly executes a read request until cancelled or the optional max count is reached.
    /// </summary>
    /// <param name="request">Polling request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async stream of read results.</returns>
    IAsyncEnumerable<ModbusReadResult> PollAsync(ModbusPollRequest request, CancellationToken cancellationToken = default);
}
