using System.Text;
using System.Text.Json;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Protocol.Parsers;

public class CustomProtocolParser : IProtocolParser
{
    private readonly ILogger _logger;

    public ProtocolType Type => ProtocolType.Custom;
    public string Name => "CustomProtocolParser";

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

            ValidateProtocolConfig(customConfig);
            if (!ValidateFrame(rawData, customConfig))
            {
                return result;
            }

            result.Add(new DeviceData
            {
                DeviceId = ParseDeviceId(rawData, customConfig),
                ChannelId = config.ChannelId,
                ProtocolType = Type,
                CollectTime = DateTime.Now,
                DataItems = ParseDataPoints(rawData, customConfig),
                RawData = rawData.ToArray(),
                IsValid = true
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to parse custom protocol payload: {ex.Message}", ex);
        }

        return result;
    }

    public byte[] Pack(DeviceData data, ProtocolConfig config)
    {
        throw new NotSupportedException("Custom protocol packing requires a protocol-specific command builder.");
    }

    public byte[] Pack(DeviceCommand command, ProtocolConfig config)
    {
        throw new NotSupportedException("Custom protocol packing requires a protocol-specific command builder.");
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

    private static void ValidateProtocolConfig(CustomProtocolConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ProtocolId))
        {
            throw new ArgumentException("ProtocolId is required.");
        }

        if (config.Points.Count == 0)
        {
            throw new ArgumentException("At least one point must be configured.");
        }
    }

    private static bool ValidateFrame(ReadOnlySpan<byte> data, CustomProtocolConfig config)
    {
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

        return true;
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
                    Timestamp = DateTime.Now,
                    IsValid = convertedValue >= point.MinValue && convertedValue <= point.MaxValue
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to parse point {point.PointName}: {ex.Message}", ex);
            }
        }

        return result;
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
}
