namespace Vktun.IoT.Connector.DeviceMock.Protocols.Modbus;

public class ModbusDataStore
{
    public bool[] Coils { get; private set; } = Array.Empty<bool>();
    public bool[] DiscreteInputs { get; private set; } = Array.Empty<bool>();
    public ushort[] InputRegisters { get; private set; } = Array.Empty<ushort>();
    public ushort[] HoldingRegisters { get; private set; } = Array.Empty<ushort>();
    
    private readonly object _lock = new();
    
    public void Initialize(int coilCount, int discreteInputCount, int inputRegisterCount, int holdingRegisterCount)
    {
        Coils = new bool[coilCount];
        DiscreteInputs = new bool[discreteInputCount];
        InputRegisters = new ushort[inputRegisterCount];
        HoldingRegisters = new ushort[holdingRegisterCount];
    }
    
    public bool GetCoil(ushort address)
    {
        lock (_lock)
        {
            if (address >= Coils.Length)
            {
                return false;
            }
            return Coils[address];
        }
    }
    
    public bool[] GetCoils(ushort startAddress, ushort quantity)
    {
        lock (_lock)
        {
            var result = new bool[quantity];
            for (int i = 0; i < quantity && (startAddress + i) < Coils.Length; i++)
            {
                result[i] = Coils[startAddress + i];
            }
            return result;
        }
    }
    
    public void SetCoil(ushort address, bool value)
    {
        lock (_lock)
        {
            if (address < Coils.Length)
            {
                Coils[address] = value;
            }
        }
    }
    
    public void SetCoils(ushort startAddress, bool[] values)
    {
        lock (_lock)
        {
            for (int i = 0; i < values.Length && (startAddress + i) < Coils.Length; i++)
            {
                Coils[startAddress + i] = values[i];
            }
        }
    }
    
    public bool GetDiscreteInput(ushort address)
    {
        lock (_lock)
        {
            if (address >= DiscreteInputs.Length)
            {
                return false;
            }
            return DiscreteInputs[address];
        }
    }
    
    public bool[] GetDiscreteInputs(ushort startAddress, ushort quantity)
    {
        lock (_lock)
        {
            var result = new bool[quantity];
            for (int i = 0; i < quantity && (startAddress + i) < DiscreteInputs.Length; i++)
            {
                result[i] = DiscreteInputs[startAddress + i];
            }
            return result;
        }
    }
    
    public ushort GetInputRegister(ushort address)
    {
        lock (_lock)
        {
            if (address >= InputRegisters.Length)
            {
                return 0;
            }
            return InputRegisters[address];
        }
    }
    
    public ushort[] GetInputRegisters(ushort startAddress, ushort quantity)
    {
        lock (_lock)
        {
            var result = new ushort[quantity];
            for (int i = 0; i < quantity && (startAddress + i) < InputRegisters.Length; i++)
            {
                result[i] = InputRegisters[startAddress + i];
            }
            return result;
        }
    }
    
    public ushort GetHoldingRegister(ushort address)
    {
        lock (_lock)
        {
            if (address >= HoldingRegisters.Length)
            {
                return 0;
            }
            return HoldingRegisters[address];
        }
    }
    
    public ushort[] GetHoldingRegisters(ushort startAddress, ushort quantity)
    {
        lock (_lock)
        {
            var result = new ushort[quantity];
            for (int i = 0; i < quantity && (startAddress + i) < HoldingRegisters.Length; i++)
            {
                result[i] = HoldingRegisters[startAddress + i];
            }
            return result;
        }
    }
    
    public void SetHoldingRegister(ushort address, ushort value)
    {
        lock (_lock)
        {
            if (address < HoldingRegisters.Length)
            {
                HoldingRegisters[address] = value;
            }
        }
    }
    
    public void SetHoldingRegisters(ushort startAddress, ushort[] values)
    {
        lock (_lock)
        {
            for (int i = 0; i < values.Length && (startAddress + i) < HoldingRegisters.Length; i++)
            {
                HoldingRegisters[startAddress + i] = values[i];
            }
        }
    }
}
