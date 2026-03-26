using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.DeviceMock.Models;

public class MockDeviceConfig
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public ProtocolType ProtocolType { get; set; }
    public CommunicationType CommunicationType { get; set; }
    public string IpAddress { get; set; } = "0.0.0.0";
    public int Port { get; set; }
    public byte SlaveId { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public List<MockDataPoint> DataPoints { get; set; } = new();
}

public class MockDataPoint
{
    public string PointName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public DataType DataType { get; set; } = DataType.UInt16;
    public object? InitialValue { get; set; }
    public DataSimulationType SimulationType { get; set; } = DataSimulationType.Static;
    public double MinValue { get; set; }
    public double MaxValue { get; set; } = 100.0;
    public double UpdateInterval { get; set; } = 1000;
}

public enum DataSimulationType
{
    Static,
    Random,
    Sine,
    Linear,
    Step
}
