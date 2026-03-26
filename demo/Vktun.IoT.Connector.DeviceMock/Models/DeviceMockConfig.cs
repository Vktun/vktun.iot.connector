namespace Vktun.IoT.Connector.DeviceMock.Models;

public class DeviceMockConfig
{
    public List<MockDeviceConfig> Devices { get; set; } = new();
}

public class ModbusMockConfig
{
    public int CoilCount { get; set; } = 10000;
    public int DiscreteInputCount { get; set; } = 10000;
    public int InputRegisterCount { get; set; } = 10000;
    public int HoldingRegisterCount { get; set; } = 10000;
}

public class S7MockConfig
{
    public int DbCount { get; set; } = 100;
    public int DbSize { get; set; } = 65536;
    public int InputSize { get; set; } = 1024;
    public int OutputSize { get; set; } = 1024;
    public int MerkerSize { get; set; } = 1024;
}

public class MitsubishiMockConfig
{
    public int DRegisterCount { get; set; } = 10000;
    public int MRegisterCount { get; set; } = 10000;
    public int XRegisterCount { get; set; } = 1000;
    public int YRegisterCount { get; set; } = 1000;
}

public class OmronMockConfig
{
    public int CioCount { get; set; } = 10000;
    public int DmCount { get; set; } = 32768;
    public int WrCount { get; set; } = 512;
}
