using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.Core.Models
{
    public class UnifiedPointAddress
    {
        public ProtocolType ProtocolType { get; set; }
        public string RawAddress { get; set; } = string.Empty;
        public string Area { get; set; } = string.Empty;
        public int Offset { get; set; }
        public int BitOffset { get; set; }
        public int Length { get; set; } = 1;
        public DataType DataType { get; set; } = DataType.UInt16;
        public ByteOrder ByteOrder { get; set; } = ByteOrder.BigEndian;
        public int SlaveId { get; set; }
        public int DbNumber { get; set; }
        public string PointName { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public double Ratio { get; set; } = 1.0;
        public double OffsetValue { get; set; }
        public double MinValue { get; set; } = double.MinValue;
        public double MaxValue { get; set; } = double.MaxValue;
        public bool IsReadOnly { get; set; } = true;
        public string Description { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;

        public override string ToString()
        {
            return ProtocolType switch
            {
                ProtocolType.ModbusRtu or ProtocolType.ModbusTcp => FormatModbusAddress(),
                ProtocolType.S7 => FormatS7Address(),
                _ => $"{ProtocolType}:{RawAddress}"
            };
        }

        private string FormatModbusAddress()
        {
            var areaPrefix = Area.ToLowerInvariant() switch
            {
                "coil" or "0x" => "0x",
                "discrete" or "1x" => "1x",
                "input" or "3x" => "3x",
                "holding" or "4x" => "4x",
                _ => Area
            };
            return $"Modbus:{SlaveId}:{areaPrefix}{Offset}:{DataType}";
        }

        private string FormatS7Address()
        {
            var areaPrefix = Area.ToLowerInvariant() switch
            {
                "db" => $"DB{DbNumber}",
                "i" or "input" => "I",
                "q" or "output" => "Q",
                "m" or "marker" => "M",
                _ => Area
            };
            return $"S7:{areaPrefix}.{Offset}.{BitOffset}:{DataType}";
        }
    }

    public static class PointAddressParser
    {
        public static UnifiedPointAddress Parse(string address, ProtocolType protocolType)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new ArgumentException("Address cannot be empty.", nameof(address));
            }

            return protocolType switch
            {
                ProtocolType.ModbusRtu or ProtocolType.ModbusTcp => ParseModbusAddress(address),
                ProtocolType.S7 => ParseS7Address(address),
                ProtocolType.IEC104 => ParseIec104Address(address),
                _ => ParseGenericAddress(address, protocolType)
            };
        }

        public static UnifiedPointAddress ParseModbusAddress(string address)
        {
            var result = new UnifiedPointAddress
            {
                ProtocolType = ProtocolType.ModbusTcp,
                RawAddress = address
            };

            var parts = address.Split(':');
            if (parts.Length >= 2)
            {
                if (int.TryParse(parts[0], out var slaveId))
                {
                    result.SlaveId = slaveId;
                    address = parts[1];
                }
            }

            if (address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || address.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
            {
                result.Area = "Coil";
                result.Offset = ParseNumber(address.Substring(2));
                result.DataType = DataType.Bool;
            }
            else if (address.StartsWith("1x", StringComparison.OrdinalIgnoreCase) || address.StartsWith("1X", StringComparison.OrdinalIgnoreCase))
            {
                result.Area = "Discrete";
                result.Offset = ParseNumber(address.Substring(2));
                result.DataType = DataType.Bool;
            }
            else if (address.StartsWith("3x", StringComparison.OrdinalIgnoreCase) || address.StartsWith("3X", StringComparison.OrdinalIgnoreCase))
            {
                result.Area = "Input";
                result.Offset = ParseNumber(address.Substring(2));
                result.DataType = DataType.UInt16;
            }
            else if (address.StartsWith("4x", StringComparison.OrdinalIgnoreCase) || address.StartsWith("4X", StringComparison.OrdinalIgnoreCase))
            {
                result.Area = "Holding";
                result.Offset = ParseNumber(address.Substring(2));
                result.DataType = DataType.UInt16;
            }
            else
            {
                result.Area = "Holding";
                result.Offset = ParseNumber(address);
                result.DataType = DataType.UInt16;
            }

            return result;
        }

        public static UnifiedPointAddress ParseS7Address(string address)
        {
            var result = new UnifiedPointAddress
            {
                ProtocolType = ProtocolType.S7,
                RawAddress = address
            };

            var span = address.AsSpan();

            if (span.StartsWith("DB", StringComparison.OrdinalIgnoreCase))
            {
                result.Area = "DB";
                var dotIndex = span.IndexOf('.');
                if (dotIndex > 2)
                {
                    result.DbNumber = int.Parse(span.Slice(2, dotIndex - 2));
                    var remainder = span.Slice(dotIndex + 1);
                    ParseS7OffsetAndType(remainder, result);
                }
            }
            else if (span.StartsWith("I", StringComparison.OrdinalIgnoreCase) || span.StartsWith("E", StringComparison.OrdinalIgnoreCase))
            {
                result.Area = "I";
                ParseS7OffsetAndType(span.Slice(1), result);
            }
            else if (span.StartsWith("Q", StringComparison.OrdinalIgnoreCase) || span.StartsWith("A", StringComparison.OrdinalIgnoreCase))
            {
                result.Area = "Q";
                ParseS7OffsetAndType(span.Slice(1), result);
            }
            else if (span.StartsWith("M", StringComparison.OrdinalIgnoreCase))
            {
                result.Area = "M";
                ParseS7OffsetAndType(span.Slice(1), result);
            }
            else
            {
                result.Area = "DB";
                result.Offset = ParseNumber(address);
            }

            return result;
        }

        public static UnifiedPointAddress ParseIec104Address(string address)
        {
            var result = new UnifiedPointAddress
            {
                ProtocolType = ProtocolType.IEC104,
                RawAddress = address,
                Area = "ASDU"
            };

            var parts = address.Split(':');
            if (parts.Length >= 2)
            {
                result.Offset = int.Parse(parts[0]);
                if (Enum.TryParse<DataType>(parts[1], true, out var dataType))
                {
                    result.DataType = dataType;
                }
            }
            else
            {
                result.Offset = ParseNumber(address);
            }

            return result;
        }

        public static UnifiedPointAddress ParseGenericAddress(string address, ProtocolType protocolType)
        {
            return new UnifiedPointAddress
            {
                ProtocolType = protocolType,
                RawAddress = address,
                Area = "Generic",
                Offset = ParseNumber(address)
            };
        }

        public static string Normalize(string address, ProtocolType protocolType)
        {
            var parsed = Parse(address, protocolType);
            return parsed.ToString();
        }

        public static DataPoint ToDataPoint(UnifiedPointAddress address, object? value)
        {
            return new DataPoint
            {
                PointName = address.PointName,
                Address = address.ToString(),
                Value = value,
                DataType = address.DataType,
                Unit = address.Unit,
                Quality = 100,
                Timestamp = DateTime.Now,
                IsValid = true
            };
        }

        public static object? ConvertValue(byte[] data, UnifiedPointAddress address)
        {
            if (data == null || data.Length == 0)
            {
                return null;
            }

            var offset = address.Offset;
            if (offset < 0 || offset >= data.Length)
            {
                return null;
            }

            return address.DataType switch
            {
                DataType.Bool => HasEnoughBytes(data, offset, 1) ? (data[offset] & (1 << Math.Clamp(address.BitOffset, 0, 7))) != 0 : null,
                DataType.UInt8 => HasEnoughBytes(data, offset, 1) ? data[offset] : null,
                DataType.Int8 => HasEnoughBytes(data, offset, 1) ? (sbyte)data[offset] : null,
                DataType.UInt16 => HasEnoughBytes(data, offset, 2) ? BitConverter.ToUInt16(EnsureByteOrder(data, offset, 2, address.ByteOrder), 0) : null,
                DataType.Int16 => HasEnoughBytes(data, offset, 2) ? BitConverter.ToInt16(EnsureByteOrder(data, offset, 2, address.ByteOrder), 0) : null,
                DataType.UInt32 => HasEnoughBytes(data, offset, 4) ? BitConverter.ToUInt32(EnsureByteOrder(data, offset, 4, address.ByteOrder), 0) : null,
                DataType.Int32 => HasEnoughBytes(data, offset, 4) ? BitConverter.ToInt32(EnsureByteOrder(data, offset, 4, address.ByteOrder), 0) : null,
                DataType.UInt64 => HasEnoughBytes(data, offset, 8) ? BitConverter.ToUInt64(EnsureByteOrder(data, offset, 8, address.ByteOrder), 0) : null,
                DataType.Int64 => HasEnoughBytes(data, offset, 8) ? BitConverter.ToInt64(EnsureByteOrder(data, offset, 8, address.ByteOrder), 0) : null,
                DataType.Float => HasEnoughBytes(data, offset, 4) ? BitConverter.ToSingle(EnsureByteOrder(data, offset, 4, address.ByteOrder), 0) : null,
                DataType.Double => HasEnoughBytes(data, offset, 8) ? BitConverter.ToDouble(EnsureByteOrder(data, offset, 8, address.ByteOrder), 0) : null,
                DataType.Bcd => HasEnoughBytes(data, offset, 1) ? FromBcd(data[offset]) : null,
                _ => data
            };
        }

        private static bool HasEnoughBytes(byte[] data, int offset, int length)
        {
            return offset >= 0 && length >= 0 && (long)offset + length <= data.Length;
        }

        public static double ApplyScaling(object? rawValue, UnifiedPointAddress address)
        {
            if (rawValue == null)
            {
                return 0;
            }

            var numericValue = Convert.ToDouble(rawValue);
            var scaled = numericValue * address.Ratio + address.OffsetValue;

            if (scaled < address.MinValue)
            {
                scaled = address.MinValue;
            }

            if (scaled > address.MaxValue)
            {
                scaled = address.MaxValue;
            }

            return scaled;
        }

        private static void ParseS7OffsetAndType(ReadOnlySpan<char> span, UnifiedPointAddress result)
        {
            var dotIndex = span.IndexOf('.');
            if (dotIndex >= 0)
            {
                result.Offset = int.Parse(span.Slice(0, dotIndex));
                result.BitOffset = int.Parse(span.Slice(dotIndex + 1));
                result.DataType = DataType.Bool;
            }
            else
            {
                var typeSuffix = DetectS7TypeSuffix(span);
                if (typeSuffix != null)
                {
                    result.Offset = int.Parse(span.Slice(0, span.Length - 1));
                    result.DataType = typeSuffix.Value;
                }
                else
                {
                    result.Offset = int.Parse(span);
                    result.DataType = DataType.UInt16;
                }
            }
        }

        private static DataType? DetectS7TypeSuffix(ReadOnlySpan<char> span)
        {
            if (span.EndsWith("B", StringComparison.OrdinalIgnoreCase))
            {
                return DataType.UInt8;
            }

            if (span.EndsWith("W", StringComparison.OrdinalIgnoreCase))
            {
                return DataType.UInt16;
            }

            if (span.EndsWith("D", StringComparison.OrdinalIgnoreCase))
            {
                return DataType.UInt32;
            }

            if (span.EndsWith("R", StringComparison.OrdinalIgnoreCase))
            {
                return DataType.Float;
            }

            return null;
        }

        private static int ParseNumber(string s)
        {
            var start = 0;
            while (start < s.Length && !char.IsDigit(s[start]))
            {
                start++;
            }

            var end = start;
            while (end < s.Length && char.IsDigit(s[end]))
            {
                end++;
            }

            return end > start ? int.Parse(s.Substring(start, end - start)) : 0;
        }

        private static byte[] EnsureByteOrder(byte[] data, int offset, int length, ByteOrder byteOrder)
        {
            if (offset < 0 || length < 0 || (long)offset + length > data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset and length exceed data bounds.");
            }

            var slice = new byte[length];
            Buffer.BlockCopy(data, offset, slice, 0, length);

            if (byteOrder == ByteOrder.BigEndian && !BitConverter.IsLittleEndian)
            {
                return slice;
            }

            if (byteOrder == ByteOrder.LittleEndian && BitConverter.IsLittleEndian)
            {
                return slice;
            }

            Array.Reverse(slice);
            return slice;
        }

        private static byte FromBcd(byte bcd)
        {
            return (byte)(((bcd >> 4) * 10) + (bcd & 0x0F));
        }
    }
}
