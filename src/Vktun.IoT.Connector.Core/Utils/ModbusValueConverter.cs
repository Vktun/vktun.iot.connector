using System.Buffers.Binary;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Core.Utils;

public static class ModbusValueConverter
{
    public static int GetByteCount(DataType dataType)
    {
        return dataType switch
        {
            DataType.Bool or DataType.UInt8 or DataType.Int8 => 1,
            DataType.UInt16 or DataType.Int16 or DataType.Bit => 2,
            DataType.UInt32 or DataType.Int32 or DataType.Float => 4,
            DataType.UInt64 or DataType.Int64 or DataType.Double => 8,
            _ => throw new NotSupportedException($"Data type {dataType} is not supported by Modbus typed conversion.")
        };
    }

    public static int GetRegisterCount(DataType dataType)
    {
        return Math.Max(1, (GetByteCount(dataType) + 1) / 2);
    }

    public static object DecodeRegisterValue(ReadOnlySpan<byte> wireBytes, DataType dataType, ByteOrder byteOrder, WordOrder wordOrder)
    {
        var byteCount = GetByteCount(dataType);
        if (wireBytes.Length < byteCount)
        {
            throw new ArgumentException($"Not enough Modbus register data for {dataType}.");
        }

        if (dataType == DataType.Bool)
        {
            return wireBytes[0] != 0;
        }

        if (dataType == DataType.UInt8)
        {
            return wireBytes[0];
        }

        if (dataType == DataType.Int8)
        {
            return unchecked((sbyte)wireBytes[0]);
        }

        Span<byte> normalized = stackalloc byte[byteCount];
        wireBytes.Slice(0, byteCount).CopyTo(normalized);
        NormalizeFromWireOrder(normalized, byteOrder, wordOrder);

        return dataType switch
        {
            DataType.UInt16 or DataType.Bit => (object)BinaryPrimitives.ReadUInt16BigEndian(normalized),
            DataType.Int16 => BinaryPrimitives.ReadInt16BigEndian(normalized),
            DataType.UInt32 => BinaryPrimitives.ReadUInt32BigEndian(normalized),
            DataType.Int32 => BinaryPrimitives.ReadInt32BigEndian(normalized),
            DataType.UInt64 => BinaryPrimitives.ReadUInt64BigEndian(normalized),
            DataType.Int64 => BinaryPrimitives.ReadInt64BigEndian(normalized),
            DataType.Float => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(normalized)),
            DataType.Double => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(normalized)),
            _ => throw new NotSupportedException($"Data type {dataType} is not supported by Modbus typed conversion.")
        };
    }

    public static ushort[] EncodeRegisterValue(object value, DataType dataType, ByteOrder byteOrder, WordOrder wordOrder)
    {
        var byteCount = Math.Max(2, GetRegisterCount(dataType) * 2);
        Span<byte> normalized = stackalloc byte[byteCount];

        switch (dataType)
        {
            case DataType.Bool:
                BinaryPrimitives.WriteUInt16BigEndian(normalized, Convert.ToBoolean(value) ? (ushort)1 : (ushort)0);
                break;
            case DataType.UInt8:
                BinaryPrimitives.WriteUInt16BigEndian(normalized, Convert.ToByte(value));
                break;
            case DataType.Int8:
                BinaryPrimitives.WriteInt16BigEndian(normalized, Convert.ToSByte(value));
                break;
            case DataType.UInt16:
            case DataType.Bit:
                BinaryPrimitives.WriteUInt16BigEndian(normalized, Convert.ToUInt16(value));
                break;
            case DataType.Int16:
                BinaryPrimitives.WriteInt16BigEndian(normalized, Convert.ToInt16(value));
                break;
            case DataType.UInt32:
                BinaryPrimitives.WriteUInt32BigEndian(normalized, Convert.ToUInt32(value));
                break;
            case DataType.Int32:
                BinaryPrimitives.WriteInt32BigEndian(normalized, Convert.ToInt32(value));
                break;
            case DataType.UInt64:
                BinaryPrimitives.WriteUInt64BigEndian(normalized, Convert.ToUInt64(value));
                break;
            case DataType.Int64:
                BinaryPrimitives.WriteInt64BigEndian(normalized, Convert.ToInt64(value));
                break;
            case DataType.Float:
                BinaryPrimitives.WriteInt32BigEndian(normalized, BitConverter.SingleToInt32Bits(Convert.ToSingle(value)));
                break;
            case DataType.Double:
                BinaryPrimitives.WriteInt64BigEndian(normalized, BitConverter.DoubleToInt64Bits(Convert.ToDouble(value)));
                break;
            default:
                throw new NotSupportedException($"Data type {dataType} is not supported by Modbus typed conversion.");
        }

        ApplyWireOrder(normalized, byteOrder, wordOrder);
        var registers = new ushort[normalized.Length / 2];
        for (var i = 0; i < registers.Length; i++)
        {
            registers[i] = BinaryPrimitives.ReadUInt16BigEndian(normalized.Slice(i * 2, 2));
        }

        return registers;
    }

    public static List<object?> DecodeCoils(ReadOnlySpan<byte> payload, ushort quantity)
    {
        var values = new List<object?>(quantity);
        for (var i = 0; i < quantity; i++)
        {
            var byteIndex = i / 8;
            var bitIndex = i % 8;
            values.Add(byteIndex < payload.Length && (payload[byteIndex] & (1 << bitIndex)) != 0);
        }

        return values;
    }

    private static void NormalizeFromWireOrder(Span<byte> bytes, ByteOrder byteOrder, WordOrder wordOrder)
    {
        if (wordOrder == WordOrder.LowWordFirst && bytes.Length > 2)
        {
            ReverseRegisterGroups(bytes);
        }

        if (byteOrder == ByteOrder.LittleEndian)
        {
            SwapBytesInRegisters(bytes);
        }
    }

    private static void ApplyWireOrder(Span<byte> bytes, ByteOrder byteOrder, WordOrder wordOrder)
    {
        if (byteOrder == ByteOrder.LittleEndian)
        {
            SwapBytesInRegisters(bytes);
        }

        if (wordOrder == WordOrder.LowWordFirst && bytes.Length > 2)
        {
            ReverseRegisterGroups(bytes);
        }
    }

    private static void SwapBytesInRegisters(Span<byte> bytes)
    {
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            (bytes[i], bytes[i + 1]) = (bytes[i + 1], bytes[i]);
        }
    }

    private static void ReverseRegisterGroups(Span<byte> bytes)
    {
        Span<byte> copy = stackalloc byte[bytes.Length];
        bytes.CopyTo(copy);
        var registerCount = bytes.Length / 2;
        for (var i = 0; i < registerCount; i++)
        {
            copy.Slice((registerCount - 1 - i) * 2, 2).CopyTo(bytes.Slice(i * 2, 2));
        }
    }
}
