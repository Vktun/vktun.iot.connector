using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.Client.Models;

public class ConnectionConfig
{
    public ProtocolType ProtocolType { get; set; }
    public CommunicationType CommunicationType { get; set; } = CommunicationType.Tcp;
    public ConnectionMode ConnectionMode { get; set; } = ConnectionMode.Client;
    public string ConnectionName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 502;
    public string LocalIpAddress { get; set; } = string.Empty;
    public int LocalPort { get; set; }
    public int Timeout { get; set; } = 3000;
    public int SendInterval { get; set; } = 1000;
    public bool AutoSend { get; set; } = false;
    
    public string PortName { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9600;
    public int DataBits { get; set; } = 8;
    public StopBits StopBits { get; set; } = StopBits.One;
    public Parity Parity { get; set; } = Parity.None;
    
    public byte SlaveId { get; set; } = 1;
    public int Rack { get; set; } = 0;
    public int Slot { get; set; } = 1;
    public string CpuType { get; set; } = "S71200";
    
    public bool IsConnected { get; set; }
    public DateTime? LastConnectTime { get; set; }
    public DateTime? LastDisconnectTime { get; set; }
}

public enum StopBits
{
    One = 1,
    OnePointFive = 2,
    Two = 3
}

public enum Parity
{
    None = 0,
    Odd = 1,
    Even = 2,
    Mark = 3,
    Space = 4
}
