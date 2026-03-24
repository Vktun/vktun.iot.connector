namespace Vktun.IoT.Connector.Core.Utils;

public static class CrcCalculator
{
    private static readonly ushort[] Crc16ModbusTable = GenerateCrc16Table(0xA001);
    private static readonly ushort[] Crc16CcittTable = GenerateCrc16Table(0x8408);
    private static readonly uint[] Crc32Table = GenerateCrc32Table();

    #region CRC16 Modbus (Poly: 0xA001, Init: 0xFFFF)

    public static ushort Crc16Modbus(byte[] data)
    {
        return Crc16Modbus(data, 0, data.Length);
    }

    public static ushort Crc16Modbus(byte[] data, int offset, int length)
    {
        ushort crc = 0xFFFF;
        
        for (int i = offset; i < offset + length; i++)
        {
            crc = (ushort)((crc >> 8) ^ Crc16ModbusTable[(crc ^ data[i]) & 0xFF]);
        }
        
        return crc;
    }

    public static ushort Crc16Modbus(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        
        for (int i = 0; i < data.Length; i++)
        {
            crc = (ushort)((crc >> 8) ^ Crc16ModbusTable[(crc ^ data[i]) & 0xFF]);
        }
        
        return crc;
    }

    #endregion

    #region CRC16 CCITT (Poly: 0x1021, Init: 0xFFFF)

    public static ushort Crc16Ccitt(byte[] data)
    {
        return Crc16Ccitt(data, 0, data.Length);
    }

    public static ushort Crc16Ccitt(byte[] data, int offset, int length)
    {
        ushort crc = 0xFFFF;
        
        for (int i = offset; i < offset + length; i++)
        {
            crc = (ushort)((crc << 8) ^ Crc16CcittTable[(crc >> 8) ^ data[i]]);
        }
        
        return crc;
    }

    public static ushort Crc16Ccitt(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        
        for (int i = 0; i < data.Length; i++)
        {
            crc = (ushort)((crc << 8) ^ Crc16CcittTable[(crc >> 8) ^ data[i]]);
        }
        
        return crc;
    }

    #endregion

    #region CRC16 XMODEM (Poly: 0x1021, Init: 0x0000)

    public static ushort Crc16Xmodem(byte[] data)
    {
        return Crc16Xmodem(data, 0, data.Length);
    }

    public static ushort Crc16Xmodem(byte[] data, int offset, int length)
    {
        ushort crc = 0x0000;
        
        for (int i = offset; i < offset + length; i++)
        {
            crc ^= (ushort)(data[i] << 8);
            
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x8000) != 0)
                {
                    crc = (ushort)((crc << 1) ^ 0x1021);
                }
                else
                {
                    crc <<= 1;
                }
            }
        }
        
        return crc;
    }

    #endregion

    #region CRC16 IBM (Poly: 0x8005, Init: 0x0000)

    public static ushort Crc16Ibm(byte[] data)
    {
        return Crc16Ibm(data, 0, data.Length);
    }

    public static ushort Crc16Ibm(byte[] data, int offset, int length)
    {
        ushort crc = 0x0000;
        
        for (int i = offset; i < offset + length; i++)
        {
            crc ^= (ushort)(data[i] << 8);
            
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x8000) != 0)
                {
                    crc = (ushort)((crc << 1) ^ 0x8005);
                }
                else
                {
                    crc <<= 1;
                }
            }
        }
        
        return crc;
    }

    #endregion

    #region CRC16 MAXIM (Poly: 0x8005, Init: 0x0000, XorOut: 0xFFFF)

    public static ushort Crc16Maxim(byte[] data)
    {
        return (ushort)(Crc16Ibm(data) ^ 0xFFFF);
    }

    #endregion

    #region CRC16 USB (Poly: 0xA001, Init: 0xFFFF, XorOut: 0xFFFF)

    public static ushort Crc16Usb(byte[] data)
    {
        return (ushort)(Crc16Modbus(data) ^ 0xFFFF);
    }

    #endregion

    #region CRC32 (Poly: 0x04C11DB7, Init: 0xFFFFFFFF)

    public static uint Crc32(byte[] data)
    {
        return Crc32(data, 0, data.Length);
    }

    public static uint Crc32(byte[] data, int offset, int length)
    {
        uint crc = 0xFFFFFFFF;
        
        for (int i = offset; i < offset + length; i++)
        {
            crc = (crc >> 8) ^ Crc32Table[(crc ^ data[i]) & 0xFF];
        }
        
        return crc ^ 0xFFFFFFFF;
    }

    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        
        for (int i = 0; i < data.Length; i++)
        {
            crc = (crc >> 8) ^ Crc32Table[(crc ^ data[i]) & 0xFF];
        }
        
        return crc ^ 0xFFFFFFFF;
    }

    #endregion

    #region CRC8

    public static byte Crc8(byte[] data)
    {
        return Crc8(data, 0, data.Length);
    }

    public static byte Crc8(byte[] data, int offset, int length)
    {
        byte crc = 0x00;
        
        for (int i = offset; i < offset + length; i++)
        {
            crc ^= data[i];
            
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x80) != 0)
                {
                    crc = (byte)((crc << 1) ^ 0x07);
                }
                else
                {
                    crc <<= 1;
                }
            }
        }
        
        return crc;
    }

    public static byte Crc8Maxim(byte[] data)
    {
        return Crc8Maxim(data, 0, data.Length);
    }

    public static byte Crc8Maxim(byte[] data, int offset, int length)
    {
        byte crc = 0x00;
        
        for (int i = offset; i < offset + length; i++)
        {
            crc ^= data[i];
            
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x01) != 0)
                {
                    crc = (byte)((crc >> 1) ^ 0x8C);
                }
                else
                {
                    crc >>= 1;
                }
            }
        }
        
        return crc;
    }

    #endregion

    #region LRC (Longitudinal Redundancy Check)

    public static byte Lrc(byte[] data)
    {
        return Lrc(data, 0, data.Length);
    }

    public static byte Lrc(byte[] data, int offset, int length)
    {
        byte lrc = 0x00;
        
        for (int i = offset; i < offset + length; i++)
        {
            lrc += data[i];
        }
        
        return (byte)((~lrc + 1) & 0xFF);
    }

    public static byte Lrc(ReadOnlySpan<byte> data)
    {
        byte lrc = 0x00;
        
        for (int i = 0; i < data.Length; i++)
        {
            lrc += data[i];
        }
        
        return (byte)((~lrc + 1) & 0xFF);
    }

    #endregion

    #region XOR Check

    public static byte XorCheck(byte[] data)
    {
        return XorCheck(data, 0, data.Length);
    }

    public static byte XorCheck(byte[] data, int offset, int length)
    {
        byte xor = 0x00;
        
        for (int i = offset; i < offset + length; i++)
        {
            xor ^= data[i];
        }
        
        return xor;
    }

    public static byte XorCheck(ReadOnlySpan<byte> data)
    {
        byte xor = 0x00;
        
        for (int i = 0; i < data.Length; i++)
        {
            xor ^= data[i];
        }
        
        return xor;
    }

    #endregion

    #region Sum Check

    public static byte SumCheck(byte[] data)
    {
        return SumCheck(data, 0, data.Length);
    }

    public static byte SumCheck(byte[] data, int offset, int length)
    {
        byte sum = 0x00;
        
        for (int i = offset; i < offset + length; i++)
        {
            sum += data[i];
        }
        
        return sum;
    }

    public static byte SumCheck(ReadOnlySpan<byte> data)
    {
        byte sum = 0x00;
        
        for (int i = 0; i < data.Length; i++)
        {
            sum += data[i];
        }
        
        return sum;
    }

    public static ushort SumCheck16(byte[] data)
    {
        return SumCheck16(data, 0, data.Length);
    }

    public static ushort SumCheck16(byte[] data, int offset, int length)
    {
        ushort sum = 0x0000;
        
        for (int i = offset; i < offset + length; i++)
        {
            sum += data[i];
        }
        
        return sum;
    }

    #endregion

    #region BCC (Block Check Character)

    public static byte Bcc(byte[] data)
    {
        return Bcc(data, 0, data.Length);
    }

    public static byte Bcc(byte[] data, int offset, int length)
    {
        byte bcc = 0x00;
        
        for (int i = offset; i < offset + length; i++)
        {
            bcc ^= data[i];
        }
        
        return bcc;
    }

    #endregion

    #region Table Generation

    private static ushort[] GenerateCrc16Table(ushort polynomial)
    {
        var table = new ushort[256];
        
        for (int i = 0; i < 256; i++)
        {
            ushort crc = (ushort)i;
            
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x0001) != 0)
                {
                    crc = (ushort)((crc >> 1) ^ polynomial);
                }
                else
                {
                    crc >>= 1;
                }
            }
            
            table[i] = crc;
        }
        
        return table;
    }

    private static uint[] GenerateCrc32Table()
    {
        var table = new uint[256];
        
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i << 24;
            
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x80000000) != 0)
                {
                    crc = (crc << 1) ^ 0x04C11DB7;
                }
                else
                {
                    crc <<= 1;
                }
            }
            
            table[i] = crc;
        }
        
        return table;
    }

    #endregion

    #region Verify Methods

    public static bool VerifyCrc16Modbus(byte[] data, ushort expectedCrc)
    {
        var calculated = Crc16Modbus(data, 0, data.Length - 2);
        var received = (ushort)((data[data.Length - 1] << 8) | data[data.Length - 2]);
        return calculated == received && calculated == expectedCrc;
    }

    public static bool VerifyCrc16Modbus(ReadOnlySpan<byte> data, ushort expectedCrc)
    {
        if (data.Length < 2) return false;
        
        var calculated = Crc16Modbus(data.Slice(0, data.Length - 2));
        var received = (ushort)((data[data.Length - 1] << 8) | data[data.Length - 2]);
        return calculated == received && calculated == expectedCrc;
    }

    public static bool VerifyCrc32(byte[] data, uint expectedCrc)
    {
        if (data.Length < 4) return false;
        
        var calculated = Crc32(data, 0, data.Length - 4);
        var received = (uint)((data[data.Length - 4] << 24) | 
                              (data[data.Length - 3] << 16) | 
                              (data[data.Length - 2] << 8) | 
                              data[data.Length - 1]);
        return calculated == received && calculated == expectedCrc;
    }

    public static bool VerifyLrc(byte[] data, byte expectedLrc)
    {
        if (data.Length < 1) return false;
        
        var calculated = Lrc(data, 0, data.Length - 1);
        return calculated == data[data.Length - 1] && calculated == expectedLrc;
    }

    #endregion
}
