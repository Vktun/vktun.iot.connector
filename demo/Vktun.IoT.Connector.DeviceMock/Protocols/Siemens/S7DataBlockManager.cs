namespace Vktun.IoT.Connector.DeviceMock.Protocols.Siemens;

public class S7DataBlockManager
{
    public Dictionary<int, byte[]> DataBlocks { get; private set; }
    public byte[] Inputs { get; private set; }
    public byte[] Outputs { get; private set; }
    public byte[] Merkers { get; private set; }
    
    private readonly object _lock = new();
    
    public S7DataBlockManager()
    {
        DataBlocks = new Dictionary<int, byte[]>();
        Inputs = Array.Empty<byte>();
        Outputs = Array.Empty<byte>();
        Merkers = Array.Empty<byte>();
    }
    
    public void Initialize(int dbCount, int dbSize, int inputSize, int outputSize, int merkerSize)
    {
        lock (_lock)
        {
            DataBlocks.Clear();
            for (int i = 1; i <= dbCount; i++)
            {
                DataBlocks[i] = new byte[dbSize];
            }
            
            Inputs = new byte[inputSize];
            Outputs = new byte[outputSize];
            Merkers = new byte[merkerSize];
        }
    }
    
    public byte[]? GetDataBlock(int dbNumber)
    {
        lock (_lock)
        {
            return DataBlocks.TryGetValue(dbNumber, out var db) ? db : null;
        }
    }
    
    public void SetDataBlock(int dbNumber, byte[] data)
    {
        lock (_lock)
        {
            if (DataBlocks.ContainsKey(dbNumber))
            {
                DataBlocks[dbNumber] = data;
            }
        }
    }
    
    public bool GetBit(string address)
    {
        lock (_lock)
        {
            var parts = address.Split('.');
            if (parts.Length < 2)
            {
                return false;
            }
            
            var area = parts[0].ToUpper();
            var byteOffset = int.Parse(parts[1]);
            var bitOffset = parts.Length > 2 ? int.Parse(parts[2]) : 0;
            
            byte[]? buffer = area switch
            {
                "I" => Inputs,
                "Q" => Outputs,
                "M" => Merkers,
                "DB" when parts.Length > 3 => GetDataBlock(int.Parse(parts[1])),
                _ => null
            };
            
            if (buffer == null)
            {
                return false;
            }
            
            if (area == "DB")
            {
                byteOffset = int.Parse(parts[2]);
                bitOffset = parts.Length > 3 ? int.Parse(parts[3]) : 0;
            }
            
            if (byteOffset >= buffer.Length)
            {
                return false;
            }
            
            return (buffer[byteOffset] & (1 << bitOffset)) != 0;
        }
    }
    
    public void SetBit(string address, bool value)
    {
        lock (_lock)
        {
            var parts = address.Split('.');
            if (parts.Length < 2)
            {
                return;
            }
            
            var area = parts[0].ToUpper();
            var byteOffset = int.Parse(parts[1]);
            var bitOffset = parts.Length > 2 ? int.Parse(parts[2]) : 0;
            
            byte[]? buffer = area switch
            {
                "I" => Inputs,
                "Q" => Outputs,
                "M" => Merkers,
                "DB" when parts.Length > 3 => GetDataBlock(int.Parse(parts[1])),
                _ => null
            };
            
            if (buffer == null)
            {
                return;
            }
            
            if (area == "DB")
            {
                byteOffset = int.Parse(parts[2]);
                bitOffset = parts.Length > 3 ? int.Parse(parts[3]) : 0;
            }
            
            if (byteOffset >= buffer.Length)
            {
                return;
            }
            
            if (value)
            {
                buffer[byteOffset] |= (byte)(1 << bitOffset);
            }
            else
            {
                buffer[byteOffset] &= (byte)~(1 << bitOffset);
            }
        }
    }
    
    public byte GetByte(string address)
    {
        lock (_lock)
        {
            var (buffer, offset) = GetBufferAndOffset(address);
            return buffer != null && offset < buffer.Length ? buffer[offset] : (byte)0;
        }
    }
    
    public void SetByte(string address, byte value)
    {
        lock (_lock)
        {
            var (buffer, offset) = GetBufferAndOffset(address);
            if (buffer != null && offset < buffer.Length)
            {
                buffer[offset] = value;
            }
        }
    }
    
    public ushort GetWord(string address)
    {
        lock (_lock)
        {
            var (buffer, offset) = GetBufferAndOffset(address);
            if (buffer == null || offset + 1 >= buffer.Length)
            {
                return 0;
            }
            
            return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
        }
    }
    
    public void SetWord(string address, ushort value)
    {
        lock (_lock)
        {
            var (buffer, offset) = GetBufferAndOffset(address);
            if (buffer == null || offset + 1 >= buffer.Length)
            {
                return;
            }
            
            buffer[offset] = (byte)(value >> 8);
            buffer[offset + 1] = (byte)(value & 0xFF);
        }
    }
    
    public uint GetDWord(string address)
    {
        lock (_lock)
        {
            var (buffer, offset) = GetBufferAndOffset(address);
            if (buffer == null || offset + 3 >= buffer.Length)
            {
                return 0;
            }
            
            return (uint)((buffer[offset] << 24) | (buffer[offset + 1] << 16) | 
                         (buffer[offset + 2] << 8) | buffer[offset + 3]);
        }
    }
    
    public void SetDWord(string address, uint value)
    {
        lock (_lock)
        {
            var (buffer, offset) = GetBufferAndOffset(address);
            if (buffer == null || offset + 3 >= buffer.Length)
            {
                return;
            }
            
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)(value & 0xFF);
        }
    }
    
    public float GetReal(string address)
    {
        lock (_lock)
        {
            var (buffer, offset) = GetBufferAndOffset(address);
            if (buffer == null || offset + 3 >= buffer.Length)
            {
                return 0;
            }
            
            var bytes = new byte[] { buffer[offset], buffer[offset + 1], buffer[offset + 2], buffer[offset + 3] };
            return BitConverter.ToSingle(bytes, 0);
        }
    }
    
    public void SetReal(string address, float value)
    {
        lock (_lock)
        {
            var (buffer, offset) = GetBufferAndOffset(address);
            if (buffer == null || offset + 3 >= buffer.Length)
            {
                return;
            }
            
            var bytes = BitConverter.GetBytes(value);
            buffer[offset] = bytes[0];
            buffer[offset + 1] = bytes[1];
            buffer[offset + 2] = bytes[2];
            buffer[offset + 3] = bytes[3];
        }
    }
    
    public byte[] GetBytes(string address, int count)
    {
        lock (_lock)
        {
            var (buffer, offset) = GetBufferAndOffset(address);
            if (buffer == null || offset + count > buffer.Length)
            {
                return Array.Empty<byte>();
            }
            
            var result = new byte[count];
            Array.Copy(buffer, offset, result, 0, count);
            return result;
        }
    }
    
    public void SetBytes(string address, byte[] data)
    {
        lock (_lock)
        {
            var (buffer, offset) = GetBufferAndOffset(address);
            if (buffer == null || offset + data.Length > buffer.Length)
            {
                return;
            }
            
            Array.Copy(data, 0, buffer, offset, data.Length);
        }
    }
    
    private (byte[]? buffer, int offset) GetBufferAndOffset(string address)
    {
        var parts = address.Split('.');
        if (parts.Length < 2)
        {
            return (null, 0);
        }
        
        var area = parts[0].ToUpper();
        var offset = int.Parse(parts[1]);
        
        byte[]? buffer = area switch
        {
            "I" => Inputs,
            "Q" => Outputs,
            "M" => Merkers,
            "DB" when parts.Length > 2 => GetDataBlock(int.Parse(parts[1])),
            _ => null
        };
        
        if (area == "DB" && parts.Length > 2)
        {
            offset = int.Parse(parts[2]);
        }
        
        return (buffer, offset);
    }
}
