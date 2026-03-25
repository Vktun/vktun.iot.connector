using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.Client.Models;

public class ProtocolTestData
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public DataType DataType { get; set; } = DataType.Int16;
    public object? Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsReadOnly { get; set; } = true;
}

public class ModbusTestData : ProtocolTestData
{
    public ModbusRegisterType RegisterType { get; set; } = ModbusRegisterType.HoldingRegister;
    public ushort Quantity { get; set; } = 1;
}

public class S7TestData : ProtocolTestData
{
    public string Area { get; set; } = "DB";
    public int DbNumber { get; set; } = 1;
    public int BitPosition { get; set; } = 0;
}

public class MitsubishiTestData : ProtocolTestData
{
    public string AreaType { get; set; } = "D";
}

public class OmronTestData : ProtocolTestData
{
    public string AreaType { get; set; } = "DM";
}

public enum ModbusRegisterType
{
    Coil = 1,
    DiscreteInput = 2,
    InputRegister = 3,
    HoldingRegister = 4
}
