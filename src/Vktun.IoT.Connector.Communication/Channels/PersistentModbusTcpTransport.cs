using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Communication.Channels;

/// <summary>
/// Persistent, serialized Modbus request/response transport.
///
/// TCP is a byte stream, so this type owns both the connection and complete frame
/// assembly. A timeout or invalid frame always discards the connection; retaining it
/// would allow a delayed response to be matched to a later command.
/// </summary>
public sealed class PersistentModbusTcpTransport : IPersistentModbusTcpTransport
{
    private const int MaxModbusFrameLength = 260;
    private readonly ConcurrentDictionary<string, EndpointConnection> _connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _idleTimeoutMs;
    private readonly bool _serializeSends;
    private readonly int _probeGateWaitTimeoutMs;
    private int _disposed;

    public PersistentModbusTcpTransport(IConfigurationProvider configurationProvider)
    {
        ArgumentNullException.ThrowIfNull(configurationProvider);
        var config = configurationProvider.GetConfig();
        _idleTimeoutMs = Math.Max(1_000, config.Tcp.SessionIdleTimeout);
        _serializeSends = config.Tcp.SerializeSends;
        _probeGateWaitTimeoutMs = Math.Max(1, config.Tcp.ProbeGateWaitTimeoutMs);
    }

    public async Task<PersistentModbusTcpResult> SendAsync(
        PersistentModbusTcpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(request.Host))
        {
            return Failed(PersistentModbusTcpOutcome.ConnectFailed, "Host is required.");
        }

        if (request.Port is <= 0 or > 65535)
        {
            return Failed(PersistentModbusTcpOutcome.ConnectFailed, "Port must be between 1 and 65535.");
        }

        if (request.Payload is not { Length: > 0 })
        {
            return Failed(PersistentModbusTcpOutcome.SendFailed, "Payload is required.");
        }

        var connection = _connections.GetOrAdd(BuildKey(request), _ => new EndpointConnection());
        var gateAcquired = false;
        if (_serializeSends)
        {
            await connection.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateAcquired = true;
        }

        var stopwatch = Stopwatch.StartNew();
        var sent = false;
        long connectMs = 0;
        long sendMs = 0;
        long receiveMs = 0;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Math.Max(1, request.TimeoutMs));

            var connectStarted = Stopwatch.GetTimestamp();
            await EnsureConnectedAsync(connection, request, timeoutCts.Token).ConfigureAwait(false);
            connectMs = (long)Stopwatch.GetElapsedTime(connectStarted).TotalMilliseconds;

            var sendStarted = Stopwatch.GetTimestamp();
            await connection.Stream!.WriteAsync(request.Payload, timeoutCts.Token).ConfigureAwait(false);
            await connection.Stream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
            sent = true;
            connection.LastActivityUtc = DateTime.UtcNow;
            sendMs = (long)Stopwatch.GetElapsedTime(sendStarted).TotalMilliseconds;

            var receiveStarted = Stopwatch.GetTimestamp();
            var response = await ReadResponseAsync(connection.Stream, request, timeoutCts.Token).ConfigureAwait(false);
            receiveMs = (long)Stopwatch.GetElapsedTime(receiveStarted).TotalMilliseconds;
            connection.LastActivityUtc = DateTime.UtcNow;
            stopwatch.Stop();
            return new PersistentModbusTcpResult
            {
                Outcome = PersistentModbusTcpOutcome.Succeeded,
                Response = response,
                Sent = true,
                ConnectElapsedMs = connectMs,
                SendElapsedMs = sendMs,
                ReceiveElapsedMs = receiveMs,
                TotalElapsedMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !sent)
        {
            await DestroyConnectionAsync(connection).ConfigureAwait(false);
            stopwatch.Stop();
            return Result(PersistentModbusTcpOutcome.Cancelled, "Request cancelled.", sent, connectMs, sendMs, receiveMs, stopwatch);
        }
        catch (OperationCanceledException)
        {
            await DestroyConnectionAsync(connection).ConfigureAwait(false);
            stopwatch.Stop();
            return Result(PersistentModbusTcpOutcome.ResponseTimeout,
                sent ? $"Response timeout after {Math.Max(1, request.TimeoutMs)}ms; the device may have executed the request." : "Connection or send timed out.",
                sent, connectMs, sendMs, receiveMs, stopwatch);
        }
        catch (EndOfStreamException ex)
        {
            await DestroyConnectionAsync(connection).ConfigureAwait(false);
            stopwatch.Stop();
            return Result(PersistentModbusTcpOutcome.RemoteClosed, ex.Message, sent, connectMs, sendMs, receiveMs, stopwatch);
        }
        catch (ModbusFrameException ex)
        {
            await DestroyConnectionAsync(connection).ConfigureAwait(false);
            stopwatch.Stop();
            return Result(PersistentModbusTcpOutcome.ProtocolError, ex.Message, sent, connectMs, sendMs, receiveMs, stopwatch);
        }
        catch (Exception ex)
        {
            await DestroyConnectionAsync(connection).ConfigureAwait(false);
            stopwatch.Stop();
            return Result(sent ? PersistentModbusTcpOutcome.SendFailed : PersistentModbusTcpOutcome.ConnectFailed,
                ex.Message, sent, connectMs, sendMs, receiveMs, stopwatch);
        }
        finally
        {
            if (gateAcquired)
            {
                connection.Gate.Release();
            }
        }
    }

    public async Task<PersistentModbusTcpResult> ProbeAsync(
        PersistentModbusTcpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(request.Host) || request.Port is <= 0 or > 65535)
        {
            return Failed(PersistentModbusTcpOutcome.ConnectFailed, "A valid host and port are required.");
        }

        var connection = _connections.GetOrAdd(BuildKey(request), _ => new EndpointConnection());
        var gateAcquired = false;
        try
        {
            var acquired = await connection.Gate.WaitAsync(TimeSpan.FromMilliseconds(_probeGateWaitTimeoutMs), cancellationToken).ConfigureAwait(false);
            if (!acquired)
            {
                return Failed(cancellationToken.IsCancellationRequested
                        ? PersistentModbusTcpOutcome.Cancelled
                        : PersistentModbusTcpOutcome.ResponseTimeout,
                    $"Probe queue wait timed out after {_probeGateWaitTimeoutMs}ms.");
            }

            gateAcquired = true;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(Math.Max(1, request.TimeoutMs));
                await EnsureConnectedAsync(connection, request, timeoutCts.Token).ConfigureAwait(false);
                connection.LastActivityUtc = DateTime.UtcNow;
                stopwatch.Stop();
                return new PersistentModbusTcpResult
                {
                    Outcome = PersistentModbusTcpOutcome.Succeeded,
                    TotalElapsedMs = stopwatch.ElapsedMilliseconds,
                };
            }
            catch (OperationCanceledException)
            {
                await DestroyConnectionAsync(connection).ConfigureAwait(false);
                stopwatch.Stop();
                return Result(cancellationToken.IsCancellationRequested
                        ? PersistentModbusTcpOutcome.Cancelled
                        : PersistentModbusTcpOutcome.ResponseTimeout,
                    "Connection probe timed out or was cancelled.", false, 0, 0, 0, stopwatch);
            }
            catch (Exception ex)
            {
                await DestroyConnectionAsync(connection).ConfigureAwait(false);
                stopwatch.Stop();
                return Result(PersistentModbusTcpOutcome.ConnectFailed, ex.Message, false, 0, 0, 0, stopwatch);
            }
        }
        catch (OperationCanceledException) when (!gateAcquired)
        {
            return Failed(cancellationToken.IsCancellationRequested
                    ? PersistentModbusTcpOutcome.Cancelled
                    : PersistentModbusTcpOutcome.ResponseTimeout,
                $"Probe queue wait timed out after {_probeGateWaitTimeoutMs}ms.");
        }
        finally
        {
            if (gateAcquired)
            {
                connection.Gate.Release();
            }
        }
    }

    public async Task ShutdownAsync()
    {
        foreach (var pair in _connections)
        {
            if (_connections.TryRemove(pair.Key, out var connection))
            {
                await connection.Gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    await DestroyConnectionAsync(connection).ConfigureAwait(false);
                }
                finally
                {
                    connection.Gate.Release();
                    connection.Gate.Dispose();
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await ShutdownAsync().ConfigureAwait(false);
        }
    }

    private async Task EnsureConnectedAsync(EndpointConnection connection, PersistentModbusTcpRequest request, CancellationToken cancellationToken)
    {
        if (connection.Client is { Connected: true } && connection.Stream is not null &&
            DateTime.UtcNow - connection.LastActivityUtc < TimeSpan.FromMilliseconds(_idleTimeoutMs))
        {
            return;
        }

        await DestroyConnectionAsync(connection).ConfigureAwait(false);
        var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        try
        {
            if (!string.IsNullOrWhiteSpace(request.LocalAddress) || request.LocalPort > 0)
            {
                var localAddress = string.IsNullOrWhiteSpace(request.LocalAddress)
                    ? IPAddress.Any
                    : IPAddress.Parse(request.LocalAddress);
                client.Client.Bind(new IPEndPoint(localAddress, Math.Max(0, request.LocalPort)));
            }

            await client.ConnectAsync(request.Host, request.Port, cancellationToken).ConfigureAwait(false);
            connection.Client = client;
            connection.Stream = client.GetStream();
            connection.LastActivityUtc = DateTime.UtcNow;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<byte[]> ReadResponseAsync(NetworkStream stream, PersistentModbusTcpRequest request, CancellationToken cancellationToken)
    {
        return request.WireFormat switch
        {
            PersistentModbusTcpWireFormat.Mbap => await ReadMbapFrameAsync(stream, request.Payload, cancellationToken).ConfigureAwait(false),
            PersistentModbusTcpWireFormat.RtuOverTcp => await ReadRtuFrameAsync(stream, request.Payload, includeCrc: false, cancellationToken).ConfigureAwait(false),
            PersistentModbusTcpWireFormat.RtuOverTcpWithCrc => await ReadRtuFrameAsync(stream, request.Payload, includeCrc: true, cancellationToken).ConfigureAwait(false),
            _ => throw new ModbusFrameException($"Unsupported wire format {request.WireFormat}.")
        };
    }

    private static async Task<byte[]> ReadMbapFrameAsync(NetworkStream stream, byte[] request, CancellationToken cancellationToken)
    {
        if (request.Length < 8)
        {
            throw new ModbusFrameException("Invalid Modbus TCP request frame.");
        }

        var header = await ReadExactlyAsync(stream, 6, cancellationToken).ConfigureAwait(false);
        if (header[2] != 0 || header[3] != 0)
        {
            throw new ModbusFrameException("Invalid Modbus TCP protocol identifier.");
        }

        var length = (header[4] << 8) | header[5];
        if (length is < 2 or > 254)
        {
            throw new ModbusFrameException($"Invalid Modbus TCP MBAP length {length}.");
        }

        var tail = await ReadExactlyAsync(stream, length, cancellationToken).ConfigureAwait(false);
        var response = header.Concat(tail).ToArray();
        if (response[0] != request[0] || response[1] != request[1])
        {
            throw new ModbusFrameException("Modbus TCP transaction id does not match the request.");
        }

        ValidateUnitAndFunction(request[6], request[7], response[6], response[7]);
        return response;
    }

    private static async Task<byte[]> ReadRtuFrameAsync(NetworkStream stream, byte[] request, bool includeCrc, CancellationToken cancellationToken)
    {
        if (request.Length < (includeCrc ? 4 : 2))
        {
            throw new ModbusFrameException("Invalid Modbus RTU-over-TCP request frame.");
        }

        var prefix = await ReadExactlyAsync(stream, 2, cancellationToken).ConfigureAwait(false);
        ValidateUnitAndFunction(request[0], request[1], prefix[0], prefix[1]);
        var isException = (prefix[1] & 0x80) != 0;
        var remaining = isException
            ? 1
            : prefix[1] switch
            {
                0x01 or 0x02 or 0x03 or 0x04 => 1,
                0x05 or 0x06 or 0x0F or 0x10 => 4,
                _ => throw new ModbusFrameException($"Unsupported Modbus RTU function code 0x{prefix[1]:X2}.")
            };

        var variable = await ReadExactlyAsync(stream, remaining, cancellationToken).ConfigureAwait(false);
        if (!isException && prefix[1] is 0x01 or 0x02 or 0x03 or 0x04)
        {
            variable = variable.Concat(await ReadExactlyAsync(stream, variable[0], cancellationToken).ConfigureAwait(false)).ToArray();
        }

        var response = prefix.Concat(variable).ToArray();
        if (includeCrc)
        {
            var crc = await ReadExactlyAsync(stream, 2, cancellationToken).ConfigureAwait(false);
            response = response.Concat(crc).ToArray();
            ValidateCrc(response);
        }

        if (response.Length > MaxModbusFrameLength)
        {
            throw new ModbusFrameException("Modbus RTU response exceeds the maximum frame length.");
        }

        return response;
    }

    private static void ValidateUnitAndFunction(byte requestUnit, byte requestFunction, byte responseUnit, byte responseFunction)
    {
        if (requestUnit != responseUnit)
        {
            throw new ModbusFrameException("Modbus station id does not match the request.");
        }

        if (responseFunction != requestFunction && responseFunction != (byte)(requestFunction | 0x80))
        {
            throw new ModbusFrameException("Modbus function code does not match the request.");
        }
    }

    private static void ValidateCrc(byte[] frame)
    {
        if (frame.Length < 5)
        {
            throw new ModbusFrameException("Modbus RTU response is too short for CRC validation.");
        }

        var expected = (ushort)(frame[^2] | (frame[^1] << 8));
        if (CalculateCrc(frame.AsSpan(0, frame.Length - 2)) != expected)
        {
            throw new ModbusFrameException("Modbus RTU response CRC validation failed.");
        }
    }

    private static ushort CalculateCrc(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
        }

        return crc;
    }

    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("TCP peer closed the connection while Modbus response was being received.");
            }

            offset += read;
        }

        return buffer;
    }

    private static async Task DestroyConnectionAsync(EndpointConnection connection)
    {
        var stream = connection.Stream;
        var client = connection.Client;
        connection.Stream = null;
        connection.Client = null;

        if (stream is not null)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }

        client?.Dispose();
    }

    private static string BuildKey(PersistentModbusTcpRequest request) =>
        $"{request.Host.Trim().ToUpperInvariant()}:{request.Port}|{request.LocalAddress?.Trim().ToUpperInvariant() ?? "*"}:{request.LocalPort}";

    private static PersistentModbusTcpResult Failed(PersistentModbusTcpOutcome outcome, string message) =>
        new() { Outcome = outcome, ErrorMessage = message };

    private static PersistentModbusTcpResult Result(PersistentModbusTcpOutcome outcome, string message, bool sent,
        long connectMs, long sendMs, long receiveMs, Stopwatch stopwatch) => new()
        {
            Outcome = outcome,
            ErrorMessage = message,
            Sent = sent,
            ConnectElapsedMs = connectMs,
            SendElapsedMs = sendMs,
            ReceiveElapsedMs = receiveMs,
            TotalElapsedMs = stopwatch.ElapsedMilliseconds
        };

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(PersistentModbusTcpTransport));
        }
    }

    private sealed class EndpointConnection
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public TcpClient? Client { get; set; }
        public NetworkStream? Stream { get; set; }
        public DateTime LastActivityUtc { get; set; } = DateTime.MinValue;
    }

    private sealed class ModbusFrameException(string message) : Exception(message);
}
