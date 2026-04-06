namespace Vktun.IoT.Connector.Core.Enums;

public enum CommunicationType
{
    Tcp,
    Udp,
    Serial,
    Can,
    FourG,
    NbIoT
}

public enum ConnectionMode
{
    Server,
    Client
}

public enum DeviceStatus
{
    Offline,
    Online,
    Connecting,
    Disconnecting,
    Error
}

public enum FrameType
{
    FixedLength,
    VariableLength,
    Separator
}

public enum ByteOrder
{
    BigEndian,
    LittleEndian
}

public enum DataType
{
    Bool,
    UInt8,
    Int8,
    UInt16,
    Int16,
    UInt32,
    Int32,
    UInt64,
    Int64,
    Float,
    Double,
    Ascii,
    UnicodeString,
    Bcd,
    Bit,
    DateTime
}

public enum CheckType
{
    None,
    XOR,
    CRC16,
    CRC32,
    LRC,
    Sum
}

public enum ProtocolType
{
    ModbusRtu,
    ModbusTcp,
    Custom,
    Mqtt,
    Http,
    S7,
    IEC104
}

public enum TaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal
}
