using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.Core.Models;

/// <summary>
/// Describes a Modbus connection that can be opened by <see cref="Interfaces.IModbusClient"/>.
/// </summary>
public class ModbusConnectionOptions
{
    /// <summary>Unique connection identifier used by later read/write requests.</summary>
    public string ConnectionId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Modbus protocol framing to use. Only ModbusTcp and ModbusRtu are supported.</summary>
    public ProtocolType ProtocolType { get; set; } = ProtocolType.ModbusTcp;

    /// <summary>Remote IP address for Modbus TCP.</summary>
    public string IpAddress { get; set; } = "127.0.0.1";

    /// <summary>Remote TCP port for Modbus TCP.</summary>
    public int Port { get; set; } = 502;

    /// <summary>Serial port name for Modbus RTU.</summary>
    public string PortName { get; set; } = "COM1";

    /// <summary>Serial baud rate for Modbus RTU.</summary>
    public int BaudRate { get; set; } = 9600;

    /// <summary>Serial data bits for Modbus RTU.</summary>
    public int DataBits { get; set; } = 8;

    /// <summary>Serial parity for Modbus RTU.</summary>
    public SerialParity Parity { get; set; } = SerialParity.None;

    /// <summary>Serial stop bits for Modbus RTU.</summary>
    public SerialStopBits StopBits { get; set; } = SerialStopBits.One;

    /// <summary>Modbus unit or slave identifier.</summary>
    public byte SlaveId { get; set; } = 1;

    /// <summary>Default byte order for multi-byte values.</summary>
    public ByteOrder ByteOrder { get; set; } = ByteOrder.BigEndian;

    /// <summary>Default word order for 32-bit and 64-bit register values.</summary>
    public WordOrder WordOrder { get; set; } = WordOrder.HighWordFirst;

    /// <summary>Default command timeout in milliseconds.</summary>
    public int Timeout { get; set; } = 3000;
}

/// <summary>
/// Describes a Modbus read request.
/// </summary>
public class ModbusReadRequest
{
    /// <summary>Connection identifier previously opened by the Modbus client.</summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>Target Modbus register area.</summary>
    public ModbusRegisterType RegisterType { get; set; } = ModbusRegisterType.HoldingRegister;

    /// <summary>Zero-based Modbus address.</summary>
    public ushort Address { get; set; }

    /// <summary>Number of coils or registers to read.</summary>
    public ushort Quantity { get; set; } = 1;

    /// <summary>Typed value projection for register reads.</summary>
    public DataType DataType { get; set; } = DataType.UInt16;

    /// <summary>Byte order for register value projection.</summary>
    public ByteOrder ByteOrder { get; set; } = ByteOrder.BigEndian;

    /// <summary>Word order for 32-bit and 64-bit register value projection.</summary>
    public WordOrder WordOrder { get; set; } = WordOrder.HighWordFirst;

    /// <summary>Command timeout in milliseconds.</summary>
    public int Timeout { get; set; } = 3000;
}

/// <summary>
/// Describes a Modbus write request.
/// </summary>
public class ModbusWriteRequest
{
    /// <summary>Connection identifier previously opened by the Modbus client.</summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>Target Modbus register area.</summary>
    public ModbusRegisterType RegisterType { get; set; } = ModbusRegisterType.HoldingRegister;

    /// <summary>Zero-based Modbus address.</summary>
    public ushort Address { get; set; }

    /// <summary>Single value to write.</summary>
    public object? Value { get; set; }

    /// <summary>Multiple values to write.</summary>
    public List<object?> Values { get; set; } = new();

    /// <summary>Value data type for holding register writes.</summary>
    public DataType DataType { get; set; } = DataType.UInt16;

    /// <summary>Byte order for register value encoding.</summary>
    public ByteOrder ByteOrder { get; set; } = ByteOrder.BigEndian;

    /// <summary>Word order for 32-bit and 64-bit register value encoding.</summary>
    public WordOrder WordOrder { get; set; } = WordOrder.HighWordFirst;

    /// <summary>Command timeout in milliseconds.</summary>
    public int Timeout { get; set; } = 3000;
}

/// <summary>
/// Describes a repeated Modbus read operation.
/// </summary>
public class ModbusPollRequest
{
    /// <summary>Read request to execute repeatedly.</summary>
    public ModbusReadRequest ReadRequest { get; set; } = new();

    /// <summary>Delay between completed reads in milliseconds.</summary>
    public int IntervalMs { get; set; } = 1000;

    /// <summary>Optional maximum number of reads before stopping.</summary>
    public int? MaxReadCount { get; set; }
}

/// <summary>
/// Base result for a Modbus operation.
/// </summary>
public class ModbusOperationResult
{
    /// <summary>Connection identifier used by the operation.</summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>True when the operation completed successfully.</summary>
    public bool Success { get; set; }

    /// <summary>Error message when the operation failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Raw Modbus request frame.</summary>
    public byte[]? RequestFrame { get; set; }

    /// <summary>Raw Modbus response frame.</summary>
    public byte[]? ResponseFrame { get; set; }

    /// <summary>Elapsed operation time.</summary>
    public TimeSpan ElapsedTime { get; set; }

    /// <summary>Operation completion timestamp.</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// Result for a Modbus read operation.
/// </summary>
public class ModbusReadResult : ModbusOperationResult
{
    /// <summary>Requested register area.</summary>
    public ModbusRegisterType RegisterType { get; set; }

    /// <summary>Requested zero-based address.</summary>
    public ushort Address { get; set; }

    /// <summary>Requested quantity.</summary>
    public ushort Quantity { get; set; }

    /// <summary>Requested data type.</summary>
    public DataType DataType { get; set; }

    /// <summary>Single typed value when the request maps to one value.</summary>
    public object? Value { get; set; }

    /// <summary>Typed values when the request maps to multiple values.</summary>
    public List<object?> Values { get; set; } = new();
}
