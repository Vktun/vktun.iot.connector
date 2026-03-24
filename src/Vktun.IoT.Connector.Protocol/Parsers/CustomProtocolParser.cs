using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Protocol.Parsers;

public class CustomProtocolParser : IProtocolParser
{
    private readonly ILogger _logger;

    public ProtocolType Type => ProtocolType.Custom;
    public string Name => "自定义协议解析器";

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
            if (!config.ParseRules.TryGetValue("CustomProtocolJson", out var protocolJson))
            {
                throw new InvalidOperationException("未找到自定义协议JSON配置");
            }
            
            var customConfig = System.Text.Json.JsonSerializer.Deserialize<CustomProtocolConfig>(protocolJson);
            if (customConfig == null)
            {
                throw new InvalidOperationException("协议配置解析失败");
            }
            
            ValidateProtocolConfig(customConfig);
            
            if (!ValidateFrame(rawData, customConfig))
            {
                return result;
            }
            
            var deviceId = ParseDeviceId(rawData, customConfig);
            var pointData = ParseDataPoints(rawData, customConfig);
            
            result.Add(new DeviceData
            {
                DeviceId = deviceId,
                ChannelId = config.ChannelId,
                ProtocolType = Type,
                CollectTime = DateTime.Now,
                DataItems = pointData,
                RawData = rawData.ToArray(),
                IsValid = true
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"自定义协议解析失败: {ex.Message}", ex);
        }
        
        return result;
    }

    public byte[] Pack(DeviceData data, ProtocolConfig config)
    {
        throw new NotImplementedException();
    }

    public byte[] Pack(DeviceCommand command, ProtocolConfig config)
    {
        throw new NotImplementedException();
    }

    public bool Validate(byte[] rawData, ProtocolConfig config)
    {
        if (!config.ParseRules.TryGetValue("CustomProtocolJson", out var protocolJson))
        {
            return false;
        }
        
        var customConfig = System.Text.Json.JsonSerializer.Deserialize<CustomProtocolConfig>(protocolJson);
        return customConfig != null && ValidateFrame(new ReadOnlySpan<byte>(rawData), customConfig);
    }

    private void ValidateProtocolConfig(CustomProtocolConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ProtocolId))
        {
            throw new ArgumentException("协议ID不能为空");
        }
        
        if (config.Points == null || config.Points.Count == 0)
        {
            throw new ArgumentException("未配置任何数据点");
        }
    }

    private bool ValidateFrame(ReadOnlySpan<byte> data, CustomProtocolConfig config)
    {
        if (config.FrameHeader != null && config.FrameHeader.Value != null)
        {
            if (data.Length < config.FrameHeader.Length)
            {
                return false;
            }
            
            for (int i = 0; i < config.FrameHeader.Length; i++)
            {
                if (data[i] != config.FrameHeader.Value[i])
                {
                    return false;
                }
            }
        }
        
        if (config.FrameTail != null && config.FrameTail.Value != null)
        {
            if (data.Length < config.FrameTail.Length)
            {
                return false;
            }
            
            var tailStart = data.Length - config.FrameTail.Length;
            for (int i = 0; i < config.FrameTail.Length; i++)
            {
                if (data[tailStart + i] != config.FrameTail.Value[i])
                {
                    return false;
                }
            }
        }
        
        return true;
    }

    private string ParseDeviceId(ReadOnlySpan<byte> data, CustomProtocolConfig config)
    {
        if (config.DeviceId == null)
        {
            return "Unknown";
        }
        
        var offset = config.DeviceId.Offset;
        var length = config.DeviceId.Length;
        
        if (offset + length > data.Length)
        {
            return "Unknown";
        }
        
        var deviceIdBytes = data.Slice(offset, length).ToArray();
        return BitConverter.ToString(deviceIdBytes).Replace("-", "");
    }

    private List<DataPoint> ParseDataPoints(ReadOnlySpan<byte> data, CustomProtocolConfig config)
    {
        var result = new List<DataPoint>();
        
        foreach (var point in config.Points)
        {
            try
            {
                var dataOffset = CalculateDataOffset(config) + point.Offset;
                
                if (dataOffset + point.Length > data.Length)
                {
                    continue;
                }
                
                var value = ParseValue(data.Slice(dataOffset, point.Length), point.DataType, config.ByteOrder);
                var convertedValue = ConvertValue(value, point.Ratio, point.OffsetValue);
                
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
                _logger.Error($"解析点位 {point.PointName} 失败: {ex.Message}", ex);
            }
        }
        
        return result;
    }

    private int CalculateDataOffset(CustomProtocolConfig config)
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

    private object ParseValue(ReadOnlySpan<byte> data, DataType dataType, ByteOrder byteOrder)
    {
        if (byteOrder == ByteOrder.BigEndian && data.Length > 1)
        {
            var reversed = data.ToArray().Reverse().ToArray();
            return ParseValueFromBytes(reversed, dataType);
        }
        
        return ParseValueFromBytes(data.ToArray(), dataType);
    }

    private object ParseValueFromBytes(byte[] data, DataType dataType)
    {
        return dataType switch
        {
            DataType.UInt8 => data[0],
            DataType.Int8 => (sbyte)data[0],
            DataType.UInt16 => BitConverter.ToUInt16(data, 0),
            DataType.Int16 => BitConverter.ToInt16(data, 0),
            DataType.UInt32 => BitConverter.ToUInt32(data, 0),
            DataType.Int32 => BitConverter.ToInt32(data, 0),
            DataType.UInt64 => BitConverter.ToUInt64(data, 0),
            DataType.Int64 => BitConverter.ToInt64(data, 0),
            DataType.Float => BitConverter.ToSingle(data, 0),
            DataType.Double => BitConverter.ToDouble(data, 0),
            DataType.Ascii => System.Text.Encoding.ASCII.GetString(data),
            _ => data
        };
    }

    private double ConvertValue(object value, double ratio, double offset)
    {
        try
        {
            var numericValue = Convert.ToDouble(value);
            return numericValue * ratio + offset;
        }
        catch
        {
            return 0;
        }
    }
}
