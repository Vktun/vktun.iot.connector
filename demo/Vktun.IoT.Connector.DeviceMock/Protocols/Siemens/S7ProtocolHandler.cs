namespace Vktun.IoT.Connector.DeviceMock.Protocols.Siemens;

public static class S7ProtocolHandler
{
    public static byte[] BuildTpktHeader(int length)
    {
        return new byte[]
        {
            0x03,
            0x00,
            (byte)(length >> 8),
            (byte)(length & 0xFF)
        };
    }
    
    public static byte[] BuildIsoHeader()
    {
        return new byte[]
        {
            0x11,
            0xE0,
            0x00,
            0x00,
            0x00,
            0x01,
            0x00,
            0xC0,
            0x01,
            0x0A,
            0xC1,
            0x02,
            0x01,
            0x00,
            0xC2,
            0x02,
            0x01,
            0x02
        };
    }
    
    public static byte[] BuildS7Header(byte pduType, ushort requestId, ushort parameterLength, ushort dataLength)
    {
        return new byte[]
        {
            0x32,
            pduType,
            (byte)(requestId >> 8),
            (byte)(requestId & 0xFF),
            (byte)(parameterLength >> 8),
            (byte)(parameterLength & 0xFF),
            (byte)(dataLength >> 8),
            (byte)(dataLength & 0xFF)
        };
    }
    
    public static byte[] BuildReadRequest(int dbNumber, int area, int address, int length, int bitLength)
    {
        var parameter = new byte[]
        {
            0x04,
            (byte)length,
            0x12,
            0x0A,
            0x10,
            (byte)bitLength,
            (byte)(address >> 16),
            (byte)(address >> 8),
            (byte)(address & 0xFF),
            (byte)(area >> 8),
            (byte)(area & 0xFF),
            (byte)(dbNumber >> 8),
            (byte)(dbNumber & 0xFF)
        };
        
        var s7Header = BuildS7Header(0x01, 0, (ushort)parameter.Length, 0);
        var result = new byte[s7Header.Length + parameter.Length];
        Array.Copy(s7Header, 0, result, 0, s7Header.Length);
        Array.Copy(parameter, 0, result, s7Header.Length, parameter.Length);
        
        return result;
    }
    
    public static byte[] BuildWriteRequest(int dbNumber, int area, int address, int length, int bitLength, byte[] data)
    {
        var parameter = new byte[]
        {
            0x05,
            0x01,
            0x12,
            0x0A,
            0x10,
            (byte)bitLength,
            (byte)(address >> 16),
            (byte)(address >> 8),
            (byte)(address & 0xFF),
            (byte)(area >> 8),
            (byte)(area & 0xFF),
            (byte)(dbNumber >> 8),
            (byte)(dbNumber & 0xFF)
        };
        
        var dataHeader = new byte[] { 0x00, 0x04, (byte)((data.Length >> 8) & 0xFF), (byte)(data.Length & 0xFF) };
        
        var s7Header = BuildS7Header(0x01, 0, (ushort)parameter.Length, (ushort)(dataHeader.Length + data.Length));
        var result = new byte[s7Header.Length + parameter.Length + dataHeader.Length + data.Length];
        
        Array.Copy(s7Header, 0, result, 0, s7Header.Length);
        Array.Copy(parameter, 0, result, s7Header.Length, parameter.Length);
        Array.Copy(dataHeader, 0, result, s7Header.Length + parameter.Length, dataHeader.Length);
        Array.Copy(data, 0, result, s7Header.Length + parameter.Length + dataHeader.Length, data.Length);
        
        return result;
    }
    
    public static byte[] BuildReadResponse(byte[] data)
    {
        var dataHeader = new byte[] { 0xFF, 0x04, (byte)((data.Length >> 8) & 0xFF), (byte)(data.Length & 0xFF) };
        
        var s7Header = BuildS7Header(0x03, 0, 0, (ushort)(dataHeader.Length + data.Length));
        var result = new byte[s7Header.Length + dataHeader.Length + data.Length];
        
        Array.Copy(s7Header, 0, result, 0, s7Header.Length);
        Array.Copy(dataHeader, 0, result, s7Header.Length, dataHeader.Length);
        Array.Copy(data, 0, result, s7Header.Length + dataHeader.Length, data.Length);
        
        return result;
    }
    
    public static byte[] BuildWriteResponse()
    {
        var dataHeader = new byte[] { 0xFF, 0x04, 0x00, 0x08 };
        
        var s7Header = BuildS7Header(0x03, 0, 0, (ushort)dataHeader.Length);
        var result = new byte[s7Header.Length + dataHeader.Length];
        
        Array.Copy(s7Header, 0, result, 0, s7Header.Length);
        Array.Copy(dataHeader, 0, result, s7Header.Length, dataHeader.Length);
        
        return result;
    }
    
    public static (int dbNumber, int area, int address, int length, int bitLength) ParseReadRequest(byte[] data, int offset)
    {
        if (data.Length < offset + 12)
        {
            return (0, 0, 0, 0, 0);
        }
        
        var length = data[offset + 1];
        var bitLength = data[offset + 5];
        var address = (data[offset + 6] << 16) | (data[offset + 7] << 8) | data[offset + 8];
        var area = (data[offset + 9] << 8) | data[offset + 10];
        var dbNumber = (data[offset + 11] << 8) | data[offset + 12];
        
        return (dbNumber, area, address, length, bitLength);
    }
    
    public static (int dbNumber, int area, int address, int length, int bitLength, byte[] data) ParseWriteRequest(byte[] data, int offset)
    {
        if (data.Length < offset + 12)
        {
            return (0, 0, 0, 0, 0, Array.Empty<byte>());
        }
        
        var bitLength = data[offset + 5];
        var address = (data[offset + 6] << 16) | (data[offset + 7] << 8) | data[offset + 8];
        var area = (data[offset + 9] << 8) | data[offset + 10];
        var dbNumber = (data[offset + 11] << 8) | data[offset + 12];
        
        var dataOffset = offset + 13 + 4;
        var dataLength = (data[offset + 15] << 8) | data[offset + 16];
        var writeData = new byte[dataLength];
        Array.Copy(data, dataOffset, writeData, 0, dataLength);
        
        return (dbNumber, area, address, 1, bitLength, writeData);
    }
    
    public static int GetAreaCode(string area)
    {
        return area.ToUpper() switch
        {
            "I" => 0x81,
            "Q" => 0x82,
            "M" => 0x83,
            "DB" => 0x84,
            "C" => 0x1C,
            "T" => 0x1D,
            _ => 0x84
        };
    }
}
