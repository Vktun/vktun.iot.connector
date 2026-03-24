using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.Core.Models;

public class ProtocolConfig
{
    public string ProtocolId { get; set; } = string.Empty;
    public string ProtocolName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ProtocolType ProtocolType { get; set; }
    public string ChannelId { get; set; } = string.Empty;
    public Dictionary<string, string> ParseRules { get; set; } = new();
    public List<PointConfig> Points { get; set; } = new();
}

public class PointConfig
{
    public string PointName { get; set; } = string.Empty;
    public int Offset { get; set; }
    public int Length { get; set; } = 1;
    public DataType DataType { get; set; }
    public double Ratio { get; set; } = 1.0;
    public double OffsetValue { get; set; }
    public string Unit { get; set; } = string.Empty;
    public double MinValue { get; set; } = double.MinValue;
    public double MaxValue { get; set; } = double.MaxValue;
    public bool IsReadOnly { get; set; } = true;
    public string Description { get; set; } = string.Empty;
}

public class CustomProtocolConfig
{
    public string ProtocolId { get; set; } = string.Empty;
    public string ProtocolName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public FrameType FrameType { get; set; }
    public ByteOrder ByteOrder { get; set; } = ByteOrder.BigEndian;
    public FrameHeaderConfig? FrameHeader { get; set; }
    public FrameLengthConfig? FrameLength { get; set; }
    public FrameDeviceIdConfig? DeviceId { get; set; }
    public FrameCheckConfig? FrameCheck { get; set; }
    public FrameTailConfig? FrameTail { get; set; }
    public List<PointConfig> Points { get; set; } = new();
}

public class FrameHeaderConfig
{
    public byte[]? Value { get; set; }
    public int Length { get; set; }
}

public class FrameLengthConfig
{
    public int Offset { get; set; }
    public int Length { get; set; } = 1;
    public string CalcRule { get; set; } = "Self";
    public int FixedLength { get; set; }
}

public class FrameDeviceIdConfig
{
    public int Offset { get; set; }
    public int Length { get; set; } = 1;
    public DataType DataType { get; set; } = DataType.UInt16;
}

public class FrameCheckConfig
{
    public CheckType CheckType { get; set; }
    public int CheckStartOffset { get; set; }
    public int CheckEndOffset { get; set; }
}

public class FrameTailConfig
{
    public byte[]? Value { get; set; }
    public int Length { get; set; }
}
