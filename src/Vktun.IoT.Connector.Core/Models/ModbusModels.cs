using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.Core.Models
{
    public enum ModbusRegisterType
    {
        Coil = 1,
        DiscreteInput = 2,
        InputRegister = 3,
        HoldingRegister = 4
    }

    public enum ModbusFunctionCode : byte
    {
        ReadCoils = 0x01,
        ReadDiscreteInputs = 0x02,
        ReadHoldingRegisters = 0x03,
        ReadInputRegisters = 0x04,
        WriteSingleCoil = 0x05,
        WriteSingleRegister = 0x06,
        WriteMultipleCoils = 0x0F,
        WriteMultipleRegisters = 0x10
    }

    public class ModbusConfig
    {
        public string ProtocolId { get; set; } = string.Empty;
        public string ProtocolName { get; set; } = "Modbus";
        public string Description { get; set; } = string.Empty;
        public ModbusType ModbusType { get; set; } = ModbusType.Rtu;
        public byte SlaveId { get; set; } = 1;
        public ByteOrder ByteOrder { get; set; } = ByteOrder.BigEndian;
        public WordOrder WordOrder { get; set; } = WordOrder.HighWordFirst;
        public List<ModbusPointConfig> Points { get; set; } = new List<ModbusPointConfig>();
        public int ResponseTimeout { get; set; } = 1000;
        public int InterFrameDelay { get; set; } = 50;
    }

    public enum ModbusType
    {
        Rtu,
        Tcp,
        Ascii
    }

    public enum WordOrder
    {
        HighWordFirst,
        LowWordFirst
    }

    public class ModbusPointConfig
    {
        public string PointName { get; set; } = string.Empty;
        public ModbusRegisterType RegisterType { get; set; }
        public ushort Address { get; set; }
        public ushort Quantity { get; set; } = 1;
        public DataType DataType { get; set; } = DataType.UInt16;
        public double Ratio { get; set; } = 1.0;
        public double OffsetValue { get; set; }
        public string Unit { get; set; } = string.Empty;
        public double MinValue { get; set; } = double.MinValue;
        public double MaxValue { get; set; } = double.MaxValue;
        public bool IsReadOnly { get; set; } = true;
        public string Description { get; set; } = string.Empty;
        public int ScanRate { get; set; } = 1000;
    }

    public class ModbusRequest
    {
        public byte SlaveId { get; set; }
        public ModbusFunctionCode FunctionCode { get; set; }
        public ushort StartAddress { get; set; }
        public ushort Quantity { get; set; }
        public byte[]? Data { get; set; }
    
        public ushort TransactionId { get; set; }
        public byte UnitId { get; set; } = 1;
    }

    public class ModbusResponse
    {
        public byte SlaveId { get; set; }
        public ModbusFunctionCode FunctionCode { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public bool IsError { get; set; }
        public byte ErrorCode { get; set; }
        public ushort TransactionId { get; set; }
    }
}
