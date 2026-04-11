using System.Text;
using System.Text.Json;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;

namespace Vktun.IoT.Connector.Protocol.Parsers;

public class CustomProtocolParser : IProtocolParser
{
    private readonly ILogger _logger;

    public ProtocolType Type => ProtocolType.Custom;
    public string Name => "CustomProtocolParser";
    public string Version => "2.0.0";
    public string Description => "通用自定义协议解析器";
    public string Vendor => "Vktun";
    public string[] SupportedDeviceModels => new[] { "*" };
    public string Author => "Vktun";
    public ParserStatus Status => ParserStatus.Stable;

    public CustomProtocolParser(ILogger logger)
    {
        _logger = logger;
    }

    public List<DeviceData> Parse(byte[] rawData, ProtocolConfig config)
    {
        return Parse(new ReadOnlySpan<byte>(rawData), config);
    }

    public List<DeviceData> Parse(ReadOnlySpan<byte> rawData, ProtocolConfig config)
    {
        var result = new List<DeviceData>();

        try
        {
            var customConfig = GetCustomConfig(config);
            if (customConfig == null)
            {
                throw new InvalidOperationException("Custom protocol configuration was not found.");
            }

            var frames = SplitFrames(rawData, customConfig);
            foreach (var frame in frames)
            {
                if (!ValidateFrame(frame, customConfig))
                {
                    result.Add(new DeviceData
                    {
                        DeviceId = ParseDeviceId(frame, customConfig),
                        ChannelId = config.ChannelId,
                        ProtocolType = Type,
                        CollectTime = DateTime.Now,
                        RawData = frame.ToArray(),
                        IsValid = false,
                        ErrorMessage = "Frame validation failed."
                    });
                    continue;
                }

                var dataPoints = ParseDataPoints(frame, customConfig);
                var rawValue = ExtractRawValues(frame, customConfig, dataPoints);

                result.Add(new DeviceData
                {
                    DeviceId = ParseDeviceId(frame, customConfig),
                    ChannelId = config.ChannelId,
                    ProtocolType = Type,
                    CollectTime = DateTime.Now,
                    DataItems = dataPoints,
                    RawData = frame.ToArray(),
                    IsValid = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to parse custom protocol payload: {ex.Message}", ex);
        }

        return result;
    }

    public byte[] Pack(DeviceData data, ProtocolConfig config)
    {
        var customConfig = GetCustomConfig(config);
        if (customConfig == null)
        {
            throw new InvalidOperationException("Custom protocol configuration was not found.");
        }

        using var stream = new MemoryStream();
        if (customConfig.FrameHeader?.Value is { Length: > 0 } header)
        {
            stream.Write(header);
        }

        if (customConfig.DeviceId != null)
        {
            var deviceIdBytes = HexStringToBytes(data.DeviceId);
            stream.Write(deviceIdBytes);
        }

        foreach (var point in data.DataItems)
        {
            var bytes = ValueToBytes(point.Value, point.DataType, customConfig.ByteOrder);
            stream.Write(bytes);
        }

        if (customConfig.FrameCheck != null && customConfig.FrameCheck.CheckType != CheckType.None)
        {
            var payload = stream.ToArray();
            var checkBytes = CalculateCheck(payload, customConfig.FrameCheck);
            stream.Write(checkBytes);
        }

        if (customConfig.FrameTail?.Value is { Length: > 0 } tail)
        {
            stream.Write(tail);
        }

        return stream.ToArray();
    }

    public byte[] Pack(DeviceCommand command, ProtocolConfig config)
    {
        var customConfig = GetCustomConfig(config);
        if (customConfig == null)
        {
            throw new InvalidOperationException("Custom protocol configuration was not found.");
        }

        using var stream = new MemoryStream();

        if (customConfig.FrameHeader?.Value is { Length: > 0 } header)
        {
            stream.Write(header);
        }

        if (customConfig.DeviceId != null && command.Parameters.TryGetValue("SlaveId", out var slaveIdObj))
        {
            var slaveId = Convert.ToByte(slaveIdObj);
            stream.WriteByte(slaveId);
        }

        if (command.Data != null && command.Data.Length > 0)
        {
            stream.Write(command.Data);
        }
        else if (command.Parameters.TryGetValue("Data", out var dataObj) && dataObj is byte[] cmdData)
        {
            stream.Write(cmdData);
        }

        var payloadSoFar = stream.ToArray();
        if (customConfig.FrameLength != null && customConfig.FrameType == FrameType.VariableLength)
        {
            var lengthBytes = EncodeLength(payloadSoFar.Length, customConfig.FrameLength, customConfig.ByteOrder);
            var result = new MemoryStream();
            if (customConfig.FrameHeader?.Value is { Length: > 0 } h)
            {
                result.Write(h);
            }

            result.Write(lengthBytes);
            result.Write(payloadSoFar, (customConfig.FrameHeader?.Value?.Length ?? 0), payloadSoFar.Length - (customConfig.FrameHeader?.Value?.Length ?? 0));

            if (customConfig.FrameCheck != null && customConfig.FrameCheck.CheckType != CheckType.None)
            {
                var checkBytes = CalculateCheck(result.ToArray(), customConfig.FrameCheck);
                result.Write(checkBytes);
            }

            if (customConfig.FrameTail?.Value is { Length: > 0 } t)
            {
                result.Write(t);
            }

            return result.ToArray();
        }

        if (customConfig.FrameCheck != null && customConfig.FrameCheck.CheckType != CheckType.None)
        {
            var checkBytes = CalculateCheck(payloadSoFar, customConfig.FrameCheck);
            stream.Write(checkBytes);
        }

        if (customConfig.FrameTail?.Value is { Length: > 0 } tail)
        {
            stream.Write(tail);
        }

        return stream.ToArray();
    }

    public bool Validate(byte[] rawData, ProtocolConfig config)
    {
        var customConfig = GetCustomConfig(config);
        return customConfig != null && ValidateFrame(rawData, customConfig);
    }

    private CustomProtocolConfig? GetCustomConfig(ProtocolConfig config)
    {
        var definition = config.GetDefinition<CustomProtocolConfig>();
        if (definition != null)
        {
            return definition;
        }

        return config.ParseRules.TryGetValue("CustomProtocolJson", out var protocolJson)
            ? JsonSerializer.Deserialize<CustomProtocolConfig>(protocolJson)
            : null;
    }

    private List<byte[]> SplitFrames(ReadOnlySpan<byte> rawData, CustomProtocolConfig config)
    {
        var frames = new List<byte[]>();

        if (config.FrameType == FrameType.Separator && config.FrameTail?.Value is { Length: > 0 } separator)
        {
            var data = rawData.ToArray();
            var start = 0;
            for (var i = 0; i <= data.Length - separator.Length; i++)
            {
                var match = true;
                for (var j = 0; j < separator.Length; j++)
                {
                    if (data[i + j] != separator[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    var frameLength = i + separator.Length - start;
                    if (frameLength > 0)
                    {
                        var frame = new byte[frameLength];
                        Buffer.BlockCopy(data, start, frame, 0, frameLength);
                        frames.Add(frame);
                    }

                    start = i + separator.Length;
                    i += separator.Length - 1;
                }
            }

            if (start < data.Length)
            {
                var remaining = new byte[data.Length - start];
                Buffer.BlockCopy(data, start, remaining, 0, remaining.Length);
                frames.Add(remaining);
            }
        }
        else if (config.FrameType == FrameType.VariableLength && config.FrameLength != null)
        {
            var offset = 0;
            while (offset < rawData.Length)
            {
                var headerLen = config.FrameHeader?.Length ?? 0;
                if (offset + headerLen + config.FrameLength.Length > rawData.Length)
                {
                    break;
                }

                if (headerLen > 0 && config.FrameHeader?.Value != null)
                {
                    var headerMatch = true;
                    for (var i = 0; i < headerLen; i++)
                    {
                        if (rawData[offset + i] != config.FrameHeader.Value[i])
                        {
                            headerMatch = false;
                            break;
                        }
                    }

                    if (!headerMatch)
                    {
                        offset++;
                        continue;
                    }
                }

                var lengthOffset = offset + headerLen;
                var framePayloadLength = DecodeLength(rawData.Slice(lengthOffset, config.FrameLength.Length), config.FrameLength, config.ByteOrder);
                var totalFrameLength = headerLen + config.FrameLength.Length + framePayloadLength;

                if (config.FrameCheck != null && config.FrameCheck.CheckType != CheckType.None)
                {
                    totalFrameLength += GetCheckLength(config.FrameCheck.CheckType);
                }

                if (config.FrameTail?.Value is { Length: > 0 })
                {
                    totalFrameLength += config.FrameTail.Length;
                }

                if (offset + totalFrameLength > rawData.Length)
                {
                    break;
                }

                var frame = new byte[totalFrameLength];
                rawData.Slice(offset, totalFrameLength).CopyTo(frame);
                frames.Add(frame);
                offset += totalFrameLength;
            }
        }
        else
        {
            frames.Add(rawData.ToArray());
        }

        return frames;
    }

    private bool ValidateFrame(ReadOnlySpan<byte> data, CustomProtocolConfig config)
    {
        if (data.Length == 0)
        {
            return false;
        }

        if (config.FrameHeader?.Value is { Length: > 0 } headerValue)
        {
            if (data.Length < config.FrameHeader.Length)
            {
                return false;
            }

            for (var index = 0; index < config.FrameHeader.Length; index++)
            {
                if (data[index] != headerValue[index])
                {
                    return false;
                }
            }
        }

        if (config.FrameTail?.Value is { Length: > 0 } tailValue)
        {
            if (data.Length < config.FrameTail.Length)
            {
                return false;
            }

            var start = data.Length - config.FrameTail.Length;
            for (var index = 0; index < config.FrameTail.Length; index++)
            {
                if (data[start + index] != tailValue[index])
                {
                    return false;
                }
            }
        }

        if (config.FrameCheck != null && config.FrameCheck.CheckType != CheckType.None)
        {
            return VerifyCheck(data, config);
        }

        return true;
    }

    private bool VerifyCheck(ReadOnlySpan<byte> data, CustomProtocolConfig config)
    {
        var checkConfig = config.FrameCheck!;
        var checkLength = GetCheckLength(checkConfig.CheckType);
        if (data.Length < checkConfig.CheckEndOffset + checkLength)
        {
            return false;
        }

        var payloadEnd = data.Length - checkLength;
        var payload = data.Slice(checkConfig.CheckStartOffset, payloadEnd - checkConfig.CheckStartOffset);
        var checkBytes = data.Slice(payloadEnd, checkLength);

        var calculated = CalculateCheck(payload.ToArray(), checkConfig);
        if (calculated.Length != checkLength)
        {
            return false;
        }

        for (var i = 0; i < checkLength; i++)
        {
            if (calculated[i] != checkBytes[i])
            {
                return false;
            }
        }

        return true;
    }

    private byte[] CalculateCheck(byte[] payload, FrameCheckConfig checkConfig)
    {
        return checkConfig.CheckType switch
        {
            CheckType.CRC16 => BitConverter.GetBytes(CrcCalculator.Crc16Modbus(payload, checkConfig.CheckStartOffset, payload.Length - checkConfig.CheckStartOffset)),
            CheckType.CRC32 => BitConverter.GetBytes(CrcCalculator.Crc32(payload, checkConfig.CheckStartOffset, payload.Length - checkConfig.CheckStartOffset)),
            CheckType.LRC => new[] { CrcCalculator.Lrc(payload, checkConfig.CheckStartOffset, payload.Length - checkConfig.CheckStartOffset) },
            CheckType.XOR => new[] { CrcCalculator.XorCheck(payload, checkConfig.CheckStartOffset, payload.Length - checkConfig.CheckStartOffset) },
            CheckType.Sum => new[] { CrcCalculator.SumCheck(payload, checkConfig.CheckStartOffset, payload.Length - checkConfig.CheckStartOffset) },
            _ => Array.Empty<byte>()
        };
    }

    private static int GetCheckLength(CheckType checkType)
    {
        return checkType switch
        {
            CheckType.CRC16 => 2,
            CheckType.CRC32 => 4,
            CheckType.LRC => 1,
            CheckType.XOR => 1,
            CheckType.Sum => 1,
            _ => 0
        };
    }

    private static string ParseDeviceId(ReadOnlySpan<byte> data, CustomProtocolConfig config)
    {
        if (config.DeviceId == null)
        {
            return "Unknown";
        }

        if (config.DeviceId.Offset + config.DeviceId.Length > data.Length)
        {
            return "Unknown";
        }

        return BitConverter.ToString(data.Slice(config.DeviceId.Offset, config.DeviceId.Length).ToArray()).Replace("-", "");
    }

    private List<DataPoint> ParseDataPoints(ReadOnlySpan<byte> data, CustomProtocolConfig config)
    {
        var result = new List<DataPoint>();
        var dataOffset = CalculateDataOffset(config);

        foreach (var point in config.Points)
        {
            try
            {
                var pointOffset = dataOffset + point.Offset;
                if (pointOffset + point.Length > data.Length)
                {
                    result.Add(new DataPoint
                    {
                        PointName = point.PointName,
                        Address = point.Offset.ToString(),
                        DataType = point.DataType,
                        Unit = point.Unit,
                        Timestamp = DateTime.Now,
                        IsValid = false
                    });
                    continue;
                }

                var rawValue = ParseValue(data.Slice(pointOffset, point.Length), point.DataType, config.ByteOrder);
                var convertedValue = ConvertValue(rawValue, point.Ratio, point.OffsetValue);

                result.Add(new DataPoint
                {
                    PointName = point.PointName,
                    Address = point.Offset.ToString(),
                    Value = convertedValue,
                    DataType = point.DataType,
                    Unit = point.Unit,
                    Quality = 100,
                    Timestamp = DateTime.Now,
                    IsValid = convertedValue >= point.MinValue && convertedValue <= point.MaxValue
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to parse point {point.PointName}: {ex.Message}", ex);
                result.Add(new DataPoint
                {
                    PointName = point.PointName,
                    Address = point.Offset.ToString(),
                    DataType = point.DataType,
                    Unit = point.Unit,
                    Timestamp = DateTime.Now,
                    IsValid = false
                });
            }
        }

        return result;
    }

    private List<DataPoint> ExtractRawValues(ReadOnlySpan<byte> data, CustomProtocolConfig config, List<DataPoint> convertedPoints)
    {
        return convertedPoints;
    }

    private static int CalculateDataOffset(CustomProtocolConfig config)
    {
        var offset = 0;

        if (config.FrameHeader != null)
        {
            offset += config.FrameHeader.Length;
        }

        if (config.FrameLength != null)
        {
            offset += config.FrameLength.Length;
        }

        if (config.DeviceId != null)
        {
            offset += config.DeviceId.Length;
        }

        return offset;
    }

    private static object ParseValue(ReadOnlySpan<byte> data, DataType dataType, ByteOrder byteOrder)
    {
        var bytes = data.ToArray();
        if (byteOrder == ByteOrder.BigEndian && bytes.Length > 1)
        {
            Array.Reverse(bytes);
        }

        return dataType switch
        {
            DataType.Bool => bytes[0] != 0,
            DataType.Bit => (bytes[0] & 0x01) != 0,
            DataType.UInt8 => bytes[0],
            DataType.Int8 => (sbyte)bytes[0],
            DataType.UInt16 => BitConverter.ToUInt16(bytes, 0),
            DataType.Int16 => BitConverter.ToInt16(bytes, 0),
            DataType.UInt32 => BitConverter.ToUInt32(bytes, 0),
            DataType.Int32 => BitConverter.ToInt32(bytes, 0),
            DataType.UInt64 => BitConverter.ToUInt64(bytes, 0),
            DataType.Int64 => BitConverter.ToInt64(bytes, 0),
            DataType.Float => BitConverter.ToSingle(bytes, 0),
            DataType.Double => BitConverter.ToDouble(bytes, 0),
            DataType.Ascii => Encoding.ASCII.GetString(bytes),
            DataType.UnicodeString => Encoding.Unicode.GetString(bytes),
            DataType.Bcd => FromBcd(bytes),
            DataType.DateTime => ParseDateTime(bytes, byteOrder),
            _ => bytes
        };
    }

    private static double ConvertValue(object value, double ratio, double offset)
    {
        try
        {
            return Convert.ToDouble(value) * ratio + offset;
        }
        catch
        {
            return 0;
        }
    }

    private static int DecodeLength(ReadOnlySpan<byte> lengthBytes, FrameLengthConfig lengthConfig, ByteOrder byteOrder)
    {
        if (lengthConfig.FixedLength > 0 && lengthConfig.CalcRule == "Fixed")
        {
            return lengthConfig.FixedLength;
        }

        var bytes = lengthBytes.ToArray();
        if (byteOrder == ByteOrder.BigEndian && bytes.Length > 1)
        {
            Array.Reverse(bytes);
        }

        var rawLength = lengthConfig.Length switch
        {
            1 => bytes[0],
            2 => BitConverter.ToUInt16(bytes, 0),
            4 => (int)BitConverter.ToUInt32(bytes, 0),
            _ => bytes[0]
        };

        return lengthConfig.CalcRule switch
        {
            "Self" => rawLength,
            "SubtractHeader" => rawLength - lengthConfig.Offset,
            _ => rawLength
        };
    }

    private static byte[] EncodeLength(int length, FrameLengthConfig lengthConfig, ByteOrder byteOrder)
    {
        byte[] bytes;
        switch (lengthConfig.Length)
        {
            case 1:
                bytes = new[] { (byte)length };
                break;
            case 2:
                bytes = BitConverter.GetBytes((ushort)length);
                break;
            case 4:
                bytes = BitConverter.GetBytes((uint)length);
                break;
            default:
                bytes = new[] { (byte)length };
                break;
        }

        if (byteOrder == ByteOrder.BigEndian && bytes.Length > 1)
        {
            Array.Reverse(bytes);
        }

        return bytes;
    }

    private static byte[] ValueToBytes(object? value, DataType dataType, ByteOrder byteOrder)
    {
        if (value == null)
        {
            return Array.Empty<byte>();
        }

        byte[] bytes = dataType switch
        {
            DataType.Bool => new[] { (byte)((bool)value ? 1 : 0) },
            DataType.UInt8 => new[] { (byte)Convert.ToDouble(value) },
            DataType.Int8 => new[] { (byte)(sbyte)Convert.ToDouble(value) },
            DataType.UInt16 => BitConverter.GetBytes((ushort)Convert.ToDouble(value)),
            DataType.Int16 => BitConverter.GetBytes((short)Convert.ToDouble(value)),
            DataType.UInt32 => BitConverter.GetBytes((uint)Convert.ToDouble(value)),
            DataType.Int32 => BitConverter.GetBytes((int)Convert.ToDouble(value)),
            DataType.UInt64 => BitConverter.GetBytes((ulong)Convert.ToDouble(value)),
            DataType.Int64 => BitConverter.GetBytes((long)Convert.ToDouble(value)),
            DataType.Float => BitConverter.GetBytes((float)Convert.ToDouble(value)),
            DataType.Double => BitConverter.GetBytes(Convert.ToDouble(value)),
            DataType.Ascii => Encoding.ASCII.GetBytes(value.ToString() ?? ""),
            _ => value is byte[] arr ? arr : Array.Empty<byte>()
        };

        if (byteOrder == ByteOrder.BigEndian && bytes.Length > 1)
        {
            Array.Reverse(bytes);
        }

        return bytes;
    }

    private static byte[] HexStringToBytes(string hex)
    {
        if (hex.Length % 2 != 0)
        {
            hex = "0" + hex;
        }

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        return bytes;
    }

    private static int FromBcd(byte[] bytes)
    {
        var result = 0;
        foreach (var b in bytes)
        {
            result = result * 100 + ((b >> 4) * 10) + (b & 0x0F);
        }

        return result;
    }

    private static DateTime ParseDateTime(byte[] bytes, ByteOrder byteOrder)
    {
        if (bytes.Length >= 8)
        {
            var ticks = BitConverter.ToInt64(bytes, 0);
            if (byteOrder == ByteOrder.BigEndian)
            {
                ticks = BitConverter.ToInt64(BitConverter.GetBytes(ticks).Reverse().ToArray(), 0);
            }

            if (ticks > 0)
            {
                return new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        if (bytes.Length >= 4)
        {
            var timestamp = BitConverter.ToInt32(bytes, 0);
            if (byteOrder == ByteOrder.BigEndian)
            {
                timestamp = BitConverter.ToInt32(BitConverter.GetBytes(timestamp).Reverse().ToArray(), 0);
            }

            if (timestamp > 0)
            {
                return DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
            }
        }

        return DateTime.Now;
    }
}
