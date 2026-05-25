using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Business.Services;

/// <summary>
/// Modbus TCP slave/server simulator for SDK validation scenarios.
/// </summary>
public sealed class ModbusSlaveServer : IModbusSlaveServer
{
    private const int MaxModbusTcpLength = 253;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<TcpClient, byte> _clients = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _serverCts;
    private Task? _acceptLoopTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModbusSlaveServer"/> class.
    /// </summary>
    /// <param name="logger">SDK logger.</param>
    public ModbusSlaveServer(ILogger logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<ModbusSlaveTrafficEntry>? TrafficReceived;

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public ModbusSlaveOptions? Options { get; private set; }

    /// <inheritdoc />
    public ModbusSlaveDataStore DataStore { get; private set; } = new();

    /// <inheritdoc />
    public Task<ModbusSlaveOperationResult> StartAsync(
        ModbusSlaveOptions options,
        ModbusSlaveDataStore? dataStore = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (IsRunning)
        {
            return Task.FromResult(Failure(options.ServerId, "Modbus slave server is already running."));
        }

        if (options.Port is < 1 or > 65535)
        {
            return Task.FromResult(Failure(options.ServerId, "Port must be between 1 and 65535."));
        }

        if (!IPAddress.TryParse(options.ListenAddress, out var address))
        {
            return Task.FromResult(Failure(options.ServerId, $"Invalid listen address: {options.ListenAddress}."));
        }

        try
        {
            Options = options;
            DataStore = dataStore ?? new ModbusSlaveDataStore(
                options.CoilCount,
                options.DiscreteInputCount,
                options.InputRegisterCount,
                options.HoldingRegisterCount);

            _serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listener = new TcpListener(address, options.Port);
            _listener.Start(Math.Max(1, options.Backlog));
            IsRunning = true;
            _acceptLoopTask = AcceptLoopAsync(_serverCts.Token);

            var localEndPoint = _listener.LocalEndpoint?.ToString();
            _logger.Info($"Modbus TCP slave server started at {localEndPoint} slaveId={options.SlaveId}.");
            return Task.FromResult(new ModbusSlaveOperationResult
            {
                ServerId = options.ServerId,
                Success = true,
                LocalEndPoint = localEndPoint,
                Timestamp = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            IsRunning = false;
            _logger.Error($"Failed to start Modbus TCP slave server: {ex.Message}", ex);
            return Task.FromResult(Failure(options.ServerId, ex.Message));
        }
    }

    /// <inheritdoc />
    public async Task<ModbusSlaveOperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        var serverId = Options?.ServerId ?? string.Empty;
        if (!IsRunning)
        {
            return new ModbusSlaveOperationResult
            {
                ServerId = serverId,
                Success = true,
                Timestamp = DateTime.Now
            };
        }

        IsRunning = false;
        _serverCts?.Cancel();
        _listener?.Stop();

        foreach (var client in _clients.Keys.ToArray())
        {
            CloseClient(client);
        }

        if (_acceptLoopTask != null)
        {
            try
            {
                await _acceptLoopTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }

        _serverCts?.Dispose();
        _serverCts = null;
        _listener = null;
        _acceptLoopTask = null;
        _logger.Info($"Modbus TCP slave server stopped. serverId={serverId}");

        return new ModbusSlaveOperationResult
        {
            ServerId = serverId,
            Success = true,
            Timestamp = DateTime.Now
        };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                if (_clients.Count >= (Options?.MaxConcurrentClients ?? 100))
                {
                    client.Close();
                    continue;
                }

                _clients.TryAdd(client, 0);
                _ = HandleClientAsync(client, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException) when (!IsRunning)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error($"Modbus slave accept loop failed: {ex.Message}", ex);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? string.Empty;

        try
        {
            await using var stream = client.GetStream();
            while (!cancellationToken.IsCancellationRequested && IsRunning && client.Connected)
            {
                var header = await ReadExactAsync(stream, 6, cancellationToken).ConfigureAwait(false);
                if (header == null)
                {
                    break;
                }

                var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
                if (length < 2 || length > MaxModbusTcpLength)
                {
                    RaiseTraffic(remoteEndPoint, header, Array.Empty<byte>(), 0, 0, false, null, "Invalid MBAP length.");
                    break;
                }

                var body = await ReadExactAsync(stream, length, cancellationToken).ConfigureAwait(false);
                if (body == null)
                {
                    break;
                }

                var request = new byte[header.Length + body.Length];
                Buffer.BlockCopy(header, 0, request, 0, header.Length);
                Buffer.BlockCopy(body, 0, request, header.Length, body.Length);

                var transactionId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
                var protocolId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
                var unitId = body[0];
                var functionCode = body[1];

                if (protocolId != 0)
                {
                    RaiseTraffic(remoteEndPoint, request, Array.Empty<byte>(), unitId, functionCode, false, null, "Invalid Modbus TCP protocol id.");
                    continue;
                }

                if (Options != null && unitId != Options.SlaveId)
                {
                    RaiseTraffic(remoteEndPoint, request, Array.Empty<byte>(), unitId, functionCode, false, null, "Unit id does not match this slave.");
                    if (Options.IgnoreMismatchedUnitId)
                    {
                        continue;
                    }
                }

                var pdu = body.AsSpan(1).ToArray();
                var responsePdu = ProcessPdu(pdu, out var success, out var exceptionCode, out var errorMessage);
                var response = BuildTcpFrame(transactionId, unitId, responsePdu);
                await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
                RaiseTraffic(remoteEndPoint, request, response, unitId, functionCode, success, exceptionCode, errorMessage);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.Error($"Modbus slave client handler failed: {ex.Message}", ex);
        }
        finally
        {
            CloseClient(client);
        }
    }

    private byte[] ProcessPdu(byte[] pdu, out bool success, out byte? exceptionCode, out string? errorMessage)
    {
        success = false;
        exceptionCode = null;
        errorMessage = null;

        if (pdu.Length == 0)
        {
            exceptionCode = 0x03;
            errorMessage = "Empty PDU.";
            return ExceptionPdu(0, exceptionCode.Value);
        }

        var functionCode = pdu[0];
        try
        {
            return functionCode switch
            {
                0x01 => ReadBits(pdu, DataStore.ReadCoils, maxQuantity: 2000, out success, out exceptionCode, out errorMessage),
                0x02 => ReadBits(pdu, DataStore.ReadDiscreteInputs, maxQuantity: 2000, out success, out exceptionCode, out errorMessage),
                0x03 => ReadRegisters(pdu, DataStore.ReadHoldingRegisters, maxQuantity: 125, out success, out exceptionCode, out errorMessage),
                0x04 => ReadRegisters(pdu, DataStore.ReadInputRegisters, maxQuantity: 125, out success, out exceptionCode, out errorMessage),
                0x05 => WriteSingleCoil(pdu, out success, out exceptionCode, out errorMessage),
                0x06 => WriteSingleRegister(pdu, out success, out exceptionCode, out errorMessage),
                0x0F => WriteMultipleCoils(pdu, out success, out exceptionCode, out errorMessage),
                0x10 => WriteMultipleRegisters(pdu, out success, out exceptionCode, out errorMessage),
                _ => Exception(functionCode, 0x01, "Unsupported Modbus function.", out exceptionCode, out errorMessage)
            };
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Exception(functionCode, 0x02, ex.Message, out exceptionCode, out errorMessage);
        }
        catch (Exception ex)
        {
            return Exception(functionCode, 0x03, ex.Message, out exceptionCode, out errorMessage);
        }
    }

    private static byte[] ReadBits(
        byte[] pdu,
        Func<ushort, ushort, bool[]> read,
        ushort maxQuantity,
        out bool success,
        out byte? exceptionCode,
        out string? errorMessage)
    {
        var functionCode = pdu[0];
        if (pdu.Length != 5)
        {
            success = false;
            return Exception(functionCode, 0x03, "Read bit request has invalid length.", out exceptionCode, out errorMessage);
        }

        var startAddress = ReadUInt16(pdu, 1);
        var quantity = ReadUInt16(pdu, 3);
        if (quantity < 1 || quantity > maxQuantity)
        {
            success = false;
            return Exception(functionCode, 0x03, "Read bit quantity is invalid.", out exceptionCode, out errorMessage);
        }

        var values = read(startAddress, quantity);
        var byteCount = (byte)((quantity + 7) / 8);
        var response = new byte[2 + byteCount];
        response[0] = functionCode;
        response[1] = byteCount;

        for (var i = 0; i < quantity; i++)
        {
            if (values[i])
            {
                response[2 + i / 8] |= (byte)(1 << (i % 8));
            }
        }

        success = true;
        exceptionCode = null;
        errorMessage = null;
        return response;
    }

    private static byte[] ReadRegisters(
        byte[] pdu,
        Func<ushort, ushort, ushort[]> read,
        ushort maxQuantity,
        out bool success,
        out byte? exceptionCode,
        out string? errorMessage)
    {
        var functionCode = pdu[0];
        if (pdu.Length != 5)
        {
            success = false;
            return Exception(functionCode, 0x03, "Read register request has invalid length.", out exceptionCode, out errorMessage);
        }

        var startAddress = ReadUInt16(pdu, 1);
        var quantity = ReadUInt16(pdu, 3);
        if (quantity < 1 || quantity > maxQuantity)
        {
            success = false;
            return Exception(functionCode, 0x03, "Read register quantity is invalid.", out exceptionCode, out errorMessage);
        }

        var values = read(startAddress, quantity);
        var response = new byte[2 + quantity * 2];
        response[0] = functionCode;
        response[1] = (byte)(quantity * 2);
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2 + i * 2, 2), values[i]);
        }

        success = true;
        exceptionCode = null;
        errorMessage = null;
        return response;
    }

    private byte[] WriteSingleCoil(byte[] pdu, out bool success, out byte? exceptionCode, out string? errorMessage)
    {
        if (pdu.Length != 5)
        {
            success = false;
            return Exception(pdu[0], 0x03, "Write single coil request has invalid length.", out exceptionCode, out errorMessage);
        }

        var address = ReadUInt16(pdu, 1);
        var value = ReadUInt16(pdu, 3);
        if (value is not (0x0000 or 0xFF00))
        {
            success = false;
            return Exception(pdu[0], 0x03, "Write single coil value is invalid.", out exceptionCode, out errorMessage);
        }

        DataStore.SetCoil(address, value == 0xFF00);
        success = true;
        exceptionCode = null;
        errorMessage = null;
        return pdu.ToArray();
    }

    private byte[] WriteSingleRegister(byte[] pdu, out bool success, out byte? exceptionCode, out string? errorMessage)
    {
        if (pdu.Length != 5)
        {
            success = false;
            return Exception(pdu[0], 0x03, "Write single register request has invalid length.", out exceptionCode, out errorMessage);
        }

        var address = ReadUInt16(pdu, 1);
        var value = ReadUInt16(pdu, 3);
        DataStore.SetHoldingRegister(address, value);
        success = true;
        exceptionCode = null;
        errorMessage = null;
        return pdu.ToArray();
    }

    private byte[] WriteMultipleCoils(byte[] pdu, out bool success, out byte? exceptionCode, out string? errorMessage)
    {
        if (pdu.Length < 6)
        {
            success = false;
            return Exception(pdu[0], 0x03, "Write multiple coils request has invalid length.", out exceptionCode, out errorMessage);
        }

        var startAddress = ReadUInt16(pdu, 1);
        var quantity = ReadUInt16(pdu, 3);
        var byteCount = pdu[5];
        var expectedByteCount = (quantity + 7) / 8;
        if (quantity < 1 || quantity > 1968 || byteCount != expectedByteCount || pdu.Length != 6 + byteCount)
        {
            success = false;
            return Exception(pdu[0], 0x03, "Write multiple coils quantity or byte count is invalid.", out exceptionCode, out errorMessage);
        }

        var values = new bool[quantity];
        for (var i = 0; i < quantity; i++)
        {
            values[i] = (pdu[6 + i / 8] & (1 << (i % 8))) != 0;
        }

        DataStore.WriteCoils(startAddress, values);
        success = true;
        exceptionCode = null;
        errorMessage = null;
        return new[] { pdu[0], pdu[1], pdu[2], pdu[3], pdu[4] };
    }

    private byte[] WriteMultipleRegisters(byte[] pdu, out bool success, out byte? exceptionCode, out string? errorMessage)
    {
        if (pdu.Length < 6)
        {
            success = false;
            return Exception(pdu[0], 0x03, "Write multiple registers request has invalid length.", out exceptionCode, out errorMessage);
        }

        var startAddress = ReadUInt16(pdu, 1);
        var quantity = ReadUInt16(pdu, 3);
        var byteCount = pdu[5];
        if (quantity < 1 || quantity > 123 || byteCount != quantity * 2 || pdu.Length != 6 + byteCount)
        {
            success = false;
            return Exception(pdu[0], 0x03, "Write multiple registers quantity or byte count is invalid.", out exceptionCode, out errorMessage);
        }

        var values = new ushort[quantity];
        for (var i = 0; i < quantity; i++)
        {
            values[i] = ReadUInt16(pdu, 6 + i * 2);
        }

        DataStore.WriteHoldingRegisters(startAddress, values);
        success = true;
        exceptionCode = null;
        errorMessage = null;
        return new[] { pdu[0], pdu[1], pdu[2], pdu[3], pdu[4] };
    }

    private static ushort ReadUInt16(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
    }

    private static byte[] Exception(
        byte functionCode,
        byte code,
        string message,
        out byte? exceptionCode,
        out string? errorMessage)
    {
        exceptionCode = code;
        errorMessage = message;
        return ExceptionPdu(functionCode, code);
    }

    private static byte[] ExceptionPdu(byte functionCode, byte code)
    {
        return new[] { (byte)(functionCode | 0x80), code };
    }

    private static byte[] BuildTcpFrame(ushort transactionId, byte unitId, byte[] pdu)
    {
        var frame = new byte[7 + pdu.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(0, 2), transactionId);
        frame[2] = 0x00;
        frame[3] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4, 2), (ushort)(pdu.Length + 1));
        frame[6] = unitId;
        Buffer.BlockCopy(pdu, 0, frame, 7, pdu.Length);
        return frame;
    }

    private static async Task<byte[]?> ReadExactAsync(NetworkStream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var totalRead = 0;
        while (totalRead < count)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return null;
            }

            totalRead += bytesRead;
        }

        return buffer;
    }

    private void RaiseTraffic(
        string remoteEndPoint,
        byte[] request,
        byte[] response,
        byte unitId,
        byte functionCode,
        bool success,
        byte? exceptionCode,
        string? errorMessage)
    {
        TrafficReceived?.Invoke(this, new ModbusSlaveTrafficEntry
        {
            ServerId = Options?.ServerId ?? string.Empty,
            ClientEndPoint = remoteEndPoint,
            UnitId = unitId,
            FunctionCode = functionCode,
            Success = success,
            ExceptionCode = exceptionCode,
            ErrorMessage = errorMessage,
            RequestFrame = request,
            ResponseFrame = response,
            Timestamp = DateTime.Now
        });
    }

    private void CloseClient(TcpClient client)
    {
        if (_clients.TryRemove(client, out _))
        {
            try
            {
                client.Close();
                client.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Debug($"Ignoring Modbus slave client close failure: {ex.Message}");
            }
        }
    }

    private static ModbusSlaveOperationResult Failure(string serverId, string errorMessage)
    {
        return new ModbusSlaveOperationResult
        {
            ServerId = serverId,
            Success = false,
            ErrorMessage = errorMessage,
            Timestamp = DateTime.Now
        };
    }
}
