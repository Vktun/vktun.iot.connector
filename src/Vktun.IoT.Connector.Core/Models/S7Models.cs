using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.Core.Models;

public enum S7Area : byte
{
    I = 0x81,
    Q = 0x82,
    M = 0x83,
    DB = 0x84
}

public enum S7DataItemType : byte
{
    Bit = 0x01,
    Byte = 0x02,
    Word = 0x04,
    DWord = 0x06,
    Real = 0x08
}

public enum S7CpuType
{
    S7200 = 0,
    S7300 = 10,
    S7400 = 20,
    S71200 = 30,
    S71500 = 40
}

public class S7Config
{
    public string ProtocolId { get; set; } = string.Empty;
    public string ProtocolName { get; set; } = "S7";
    public string Description { get; set; } = string.Empty;
    public ProtocolType ProtocolType { get; set; } = ProtocolType.S7;
    public S7CpuType CpuType { get; set; } = S7CpuType.S71200;
    public int Rack { get; set; } = 0;
    public int Slot { get; set; } = 1;
    public int Port { get; set; } = 102;
    public int PduSize { get; set; } = 480;
    public ByteOrder ByteOrder { get; set; } = ByteOrder.BigEndian;
    public WordOrder WordOrder { get; set; } = WordOrder.HighWordFirst;
    public List<S7PointConfig> Points { get; set; } = new();
    public int ResponseTimeout { get; set; } = 3000;
    public int RetryCount { get; set; } = 3;
    public int RetryDelay { get; set; } = 1000;
}

public class S7PointConfig
{
    public string PointName { get; set; } = string.Empty;
    public string Area { get; set; } = "DB";
    public int DbNumber { get; set; } = 1;
    public int StartAddress { get; set; } = 0;
    public int BitPosition { get; set; } = 0;
    public DataType DataType { get; set; } = DataType.Float;
    public double Ratio { get; set; } = 1.0;
    public double OffsetValue { get; set; } = 0;
    public string Unit { get; set; } = string.Empty;
    public double MinValue { get; set; } = double.MinValue;
    public double MaxValue { get; set; } = double.MaxValue;
    public bool IsReadOnly { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public int ScanRate { get; set; } = 1000;
}

public class S7DataItem
{
    public S7Area Area { get; set; }
    public int DbNumber { get; set; }
    public int StartAddress { get; set; }
    public int BitPosition { get; set; }
    public S7DataItemType Type { get; set; }
    public int Length { get; set; }
    public byte[]? Data { get; set; }
    public object? Value { get; set; }
}

public class S7ReadRequest
{
    public List<S7DataItem> Items { get; set; } = new();
}

public class S7WriteRequest
{
    public List<S7DataItem> Items { get; set; } = new();
}

public class S7Response
{
    public bool Success { get; set; }
    public byte ErrorCode { get; set; }
    public List<S7DataItem> Items { get; set; } = new();
    public byte[]? RawData { get; set; }
}

public class S7ConnectionInfo
{
    public string IpAddress { get; set; } = "192.168.0.1";
    public int Port { get; set; } = 102;
    public S7CpuType CpuType { get; set; } = S7CpuType.S71200;
    public int Rack { get; set; } = 0;
    public int Slot { get; set; } = 1;
    public int PduSize { get; set; } = 480;
    public bool IsConnected { get; set; }
}

public class S7Header
{
    public byte ProtocolId { get; set; } = 0x32;
    public byte MessageType { get; set; }
    public ushort Reserved { get; set; } = 0x0000;
    public ushort PduReference { get; set; }
    public ushort ParameterLength { get; set; }
    public ushort DataLength { get; set; }
    public byte ErrorClass { get; set; }
    public byte ErrorCode { get; set; }
}

public class S7Parameter
{
    public byte FunctionCode { get; set; }
    public byte ItemCount { get; set; }
}

public class S7DataHeader
{
    public byte ReturnCode { get; set; }
    public byte TransportSize { get; set; }
    public ushort DataLength { get; set; }
}
