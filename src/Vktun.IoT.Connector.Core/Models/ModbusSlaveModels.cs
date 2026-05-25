namespace Vktun.IoT.Connector.Core.Models;

/// <summary>
/// Identifies one of the four Modbus slave data areas.
/// </summary>
public enum ModbusSlaveRegisterArea
{
    /// <summary>Read/write coil bits.</summary>
    Coils,

    /// <summary>Read-only discrete input bits.</summary>
    DiscreteInputs,

    /// <summary>Read-only input registers.</summary>
    InputRegisters,

    /// <summary>Read/write holding registers.</summary>
    HoldingRegisters
}

/// <summary>
/// Options for starting a Modbus TCP slave server.
/// </summary>
public class ModbusSlaveOptions
{
    /// <summary>Logical server identifier.</summary>
    public string ServerId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Local address to bind. Use 0.0.0.0 to listen on all IPv4 interfaces.</summary>
    public string ListenAddress { get; set; } = "127.0.0.1";

    /// <summary>Local TCP port to listen on.</summary>
    public int Port { get; set; } = 502;

    /// <summary>Modbus unit identifier served by this slave.</summary>
    public byte SlaveId { get; set; } = 1;

    /// <summary>Maximum pending TCP accept backlog.</summary>
    public int Backlog { get; set; } = 100;

    /// <summary>Maximum concurrently tracked TCP clients.</summary>
    public int MaxConcurrentClients { get; set; } = 100;

    /// <summary>Number of coil bits in the default data store.</summary>
    public int CoilCount { get; set; } = 10000;

    /// <summary>Number of discrete input bits in the default data store.</summary>
    public int DiscreteInputCount { get; set; } = 10000;

    /// <summary>Number of input registers in the default data store.</summary>
    public int InputRegisterCount { get; set; } = 10000;

    /// <summary>Number of holding registers in the default data store.</summary>
    public int HoldingRegisterCount { get; set; } = 10000;

    /// <summary>When true, requests for other unit identifiers are ignored without a response.</summary>
    public bool IgnoreMismatchedUnitId { get; set; } = true;
}

/// <summary>
/// Result returned by Modbus slave server lifecycle operations.
/// </summary>
public class ModbusSlaveOperationResult
{
    /// <summary>Logical server identifier.</summary>
    public string ServerId { get; set; } = string.Empty;

    /// <summary>True when the operation completed successfully.</summary>
    public bool Success { get; set; }

    /// <summary>Error message when the operation failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Local listening endpoint when available.</summary>
    public string? LocalEndPoint { get; set; }

    /// <summary>Operation completion timestamp.</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// Captures one Modbus slave request/response exchange.
/// </summary>
public class ModbusSlaveTrafficEntry : EventArgs
{
    /// <summary>Logical server identifier.</summary>
    public string ServerId { get; set; } = string.Empty;

    /// <summary>Remote TCP client endpoint.</summary>
    public string ClientEndPoint { get; set; } = string.Empty;

    /// <summary>Modbus unit identifier in the request.</summary>
    public byte UnitId { get; set; }

    /// <summary>Original Modbus function code in the request.</summary>
    public byte FunctionCode { get; set; }

    /// <summary>True when a normal response was produced.</summary>
    public bool Success { get; set; }

    /// <summary>Modbus exception code when an exception response was produced.</summary>
    public byte? ExceptionCode { get; set; }

    /// <summary>Error message when no normal response was produced.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Raw Modbus TCP request frame.</summary>
    public byte[] RequestFrame { get; set; } = Array.Empty<byte>();

    /// <summary>Raw Modbus TCP response frame. Empty when no response was sent.</summary>
    public byte[] ResponseFrame { get; set; } = Array.Empty<byte>();

    /// <summary>Timestamp for the exchange.</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// Thread-safe in-memory Modbus slave data store.
/// </summary>
public class ModbusSlaveDataStore
{
    private readonly object _lock = new();
    private readonly bool[] _coils;
    private readonly bool[] _discreteInputs;
    private readonly ushort[] _inputRegisters;
    private readonly ushort[] _holdingRegisters;

    /// <summary>
    /// Initializes a new Modbus slave data store.
    /// </summary>
    /// <param name="coilCount">Number of coil bits.</param>
    /// <param name="discreteInputCount">Number of discrete input bits.</param>
    /// <param name="inputRegisterCount">Number of input registers.</param>
    /// <param name="holdingRegisterCount">Number of holding registers.</param>
    public ModbusSlaveDataStore(
        int coilCount = 10000,
        int discreteInputCount = 10000,
        int inputRegisterCount = 10000,
        int holdingRegisterCount = 10000)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(coilCount);
        ArgumentOutOfRangeException.ThrowIfNegative(discreteInputCount);
        ArgumentOutOfRangeException.ThrowIfNegative(inputRegisterCount);
        ArgumentOutOfRangeException.ThrowIfNegative(holdingRegisterCount);

        _coils = new bool[coilCount];
        _discreteInputs = new bool[discreteInputCount];
        _inputRegisters = new ushort[inputRegisterCount];
        _holdingRegisters = new ushort[holdingRegisterCount];
    }

    /// <summary>Number of coil bits.</summary>
    public int CoilCount => _coils.Length;

    /// <summary>Number of discrete input bits.</summary>
    public int DiscreteInputCount => _discreteInputs.Length;

    /// <summary>Number of input registers.</summary>
    public int InputRegisterCount => _inputRegisters.Length;

    /// <summary>Number of holding registers.</summary>
    public int HoldingRegisterCount => _holdingRegisters.Length;

    /// <summary>Reads one coil bit.</summary>
    /// <param name="address">Zero-based coil address.</param>
    /// <returns>The coil value.</returns>
    public bool GetCoil(ushort address)
    {
        lock (_lock)
        {
            ValidateRange(address, 1, _coils.Length, nameof(address));
            return _coils[address];
        }
    }

    /// <summary>Writes one coil bit.</summary>
    /// <param name="address">Zero-based coil address.</param>
    /// <param name="value">Value to write.</param>
    public void SetCoil(ushort address, bool value)
    {
        lock (_lock)
        {
            ValidateRange(address, 1, _coils.Length, nameof(address));
            _coils[address] = value;
        }
    }

    /// <summary>Reads a range of coil bits.</summary>
    /// <param name="startAddress">Zero-based start address.</param>
    /// <param name="quantity">Number of bits to read.</param>
    /// <returns>Copied coil values.</returns>
    public bool[] ReadCoils(ushort startAddress, ushort quantity)
    {
        lock (_lock)
        {
            ValidateRange(startAddress, quantity, _coils.Length, nameof(quantity));
            return CopyRange(_coils, startAddress, quantity);
        }
    }

    /// <summary>Writes a range of coil bits.</summary>
    /// <param name="startAddress">Zero-based start address.</param>
    /// <param name="values">Values to write.</param>
    public void WriteCoils(ushort startAddress, IReadOnlyList<bool> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        lock (_lock)
        {
            ValidateRange(startAddress, values.Count, _coils.Length, nameof(values));
            for (var i = 0; i < values.Count; i++)
            {
                _coils[startAddress + i] = values[i];
            }
        }
    }

    /// <summary>Reads one discrete input bit.</summary>
    /// <param name="address">Zero-based discrete input address.</param>
    /// <returns>The discrete input value.</returns>
    public bool GetDiscreteInput(ushort address)
    {
        lock (_lock)
        {
            ValidateRange(address, 1, _discreteInputs.Length, nameof(address));
            return _discreteInputs[address];
        }
    }

    /// <summary>Seeds one discrete input bit.</summary>
    /// <param name="address">Zero-based discrete input address.</param>
    /// <param name="value">Value to set.</param>
    public void SetDiscreteInput(ushort address, bool value)
    {
        lock (_lock)
        {
            ValidateRange(address, 1, _discreteInputs.Length, nameof(address));
            _discreteInputs[address] = value;
        }
    }

    /// <summary>Reads a range of discrete input bits.</summary>
    /// <param name="startAddress">Zero-based start address.</param>
    /// <param name="quantity">Number of bits to read.</param>
    /// <returns>Copied discrete input values.</returns>
    public bool[] ReadDiscreteInputs(ushort startAddress, ushort quantity)
    {
        lock (_lock)
        {
            ValidateRange(startAddress, quantity, _discreteInputs.Length, nameof(quantity));
            return CopyRange(_discreteInputs, startAddress, quantity);
        }
    }

    /// <summary>Seeds a range of discrete input bits.</summary>
    /// <param name="startAddress">Zero-based start address.</param>
    /// <param name="values">Values to set.</param>
    public void WriteDiscreteInputs(ushort startAddress, IReadOnlyList<bool> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        lock (_lock)
        {
            ValidateRange(startAddress, values.Count, _discreteInputs.Length, nameof(values));
            for (var i = 0; i < values.Count; i++)
            {
                _discreteInputs[startAddress + i] = values[i];
            }
        }
    }

    /// <summary>Reads one input register.</summary>
    /// <param name="address">Zero-based input register address.</param>
    /// <returns>The register value.</returns>
    public ushort GetInputRegister(ushort address)
    {
        lock (_lock)
        {
            ValidateRange(address, 1, _inputRegisters.Length, nameof(address));
            return _inputRegisters[address];
        }
    }

    /// <summary>Seeds one input register.</summary>
    /// <param name="address">Zero-based input register address.</param>
    /// <param name="value">Value to set.</param>
    public void SetInputRegister(ushort address, ushort value)
    {
        lock (_lock)
        {
            ValidateRange(address, 1, _inputRegisters.Length, nameof(address));
            _inputRegisters[address] = value;
        }
    }

    /// <summary>Reads a range of input registers.</summary>
    /// <param name="startAddress">Zero-based start address.</param>
    /// <param name="quantity">Number of registers to read.</param>
    /// <returns>Copied input register values.</returns>
    public ushort[] ReadInputRegisters(ushort startAddress, ushort quantity)
    {
        lock (_lock)
        {
            ValidateRange(startAddress, quantity, _inputRegisters.Length, nameof(quantity));
            return CopyRange(_inputRegisters, startAddress, quantity);
        }
    }

    /// <summary>Seeds a range of input registers.</summary>
    /// <param name="startAddress">Zero-based start address.</param>
    /// <param name="values">Values to set.</param>
    public void WriteInputRegisters(ushort startAddress, IReadOnlyList<ushort> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        lock (_lock)
        {
            ValidateRange(startAddress, values.Count, _inputRegisters.Length, nameof(values));
            for (var i = 0; i < values.Count; i++)
            {
                _inputRegisters[startAddress + i] = values[i];
            }
        }
    }

    /// <summary>Reads one holding register.</summary>
    /// <param name="address">Zero-based holding register address.</param>
    /// <returns>The register value.</returns>
    public ushort GetHoldingRegister(ushort address)
    {
        lock (_lock)
        {
            ValidateRange(address, 1, _holdingRegisters.Length, nameof(address));
            return _holdingRegisters[address];
        }
    }

    /// <summary>Writes one holding register.</summary>
    /// <param name="address">Zero-based holding register address.</param>
    /// <param name="value">Value to write.</param>
    public void SetHoldingRegister(ushort address, ushort value)
    {
        lock (_lock)
        {
            ValidateRange(address, 1, _holdingRegisters.Length, nameof(address));
            _holdingRegisters[address] = value;
        }
    }

    /// <summary>Reads a range of holding registers.</summary>
    /// <param name="startAddress">Zero-based start address.</param>
    /// <param name="quantity">Number of registers to read.</param>
    /// <returns>Copied holding register values.</returns>
    public ushort[] ReadHoldingRegisters(ushort startAddress, ushort quantity)
    {
        lock (_lock)
        {
            ValidateRange(startAddress, quantity, _holdingRegisters.Length, nameof(quantity));
            return CopyRange(_holdingRegisters, startAddress, quantity);
        }
    }

    /// <summary>Writes a range of holding registers.</summary>
    /// <param name="startAddress">Zero-based start address.</param>
    /// <param name="values">Values to write.</param>
    public void WriteHoldingRegisters(ushort startAddress, IReadOnlyList<ushort> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        lock (_lock)
        {
            ValidateRange(startAddress, values.Count, _holdingRegisters.Length, nameof(values));
            for (var i = 0; i < values.Count; i++)
            {
                _holdingRegisters[startAddress + i] = values[i];
            }
        }
    }

    private static T[] CopyRange<T>(T[] source, ushort startAddress, ushort quantity)
    {
        var result = new T[quantity];
        Array.Copy(source, startAddress, result, 0, quantity);
        return result;
    }

    private static void ValidateRange(int startAddress, int quantity, int length, string parameterName)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, quantity, "Quantity must be greater than zero.");
        }

        if (startAddress < 0 || startAddress >= length || startAddress + quantity > length)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"Address range {startAddress}..{startAddress + quantity - 1} is outside the configured data store length {length}.");
        }
    }
}
