using System.Text;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Protocol.Parsers;

/// <summary>
/// BACnet配置
/// </summary>
public class BacnetConfig
{
    public uint DeviceId { get; set; } = 0;
    public ushort NetworkNumber { get; set; } = 0;
    public byte[]? MacAddress { get; set; }
    public int Port { get; set; } = 47808;
    public int MaxApduLength { get; set; } = 1476;
    public int SegmentTimeout { get; set; } = 5000;
    public bool SegmentationSupported { get; set; } = true;
}

/// <summary>
/// BACnet对象类型
/// </summary>
public enum BacnetObjectType
{
    AnalogInput = 0, AnalogOutput = 1, AnalogValue = 2,
    BinaryInput = 3, BinaryOutput = 4, BinaryValue = 5,
    Calendar = 6, Command = 7, Device = 8,
    MultiStateInput = 13, MultiStateOutput = 14,
    Loop = 12, TrendLog = 19, LightingOutput = 55
}

/// <summary>
/// BACnet属性ID
/// </summary>
public enum BacnetPropertyId
{
    PresentValue = 85,
    Description = 22,
    ObjectName = 68,
    StatusFlags = 97
}

/// <summary>
/// BACnet协议解析器 - 支持楼宇自动化设备数据采集
/// </summary>
public class BacnetProtocolParser : IProtocolParser
{
    private readonly ILogger _logger;

    public ProtocolType Type => ProtocolType.Custom;
    public string Name => "BACnet";

    private const byte BacnetConfirmedRequest = 0x00;
    private const byte BacnetUnconfirmedRequest = 0x10;
    private const byte BacnetComplexAck = 0x20;
    private const byte BacnetServiceReadProperty = 0x0C;
    private const byte BacnetServiceWriteProperty = 0x0E;
    private const byte BacnetServiceSubscribeCOV = 0x09;
    private const byte BacnetServiceIAm = 0x00;

    public BacnetProtocolParser(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public List<DeviceData> Parse(byte[] rawData, ProtocolConfig config)
    {
        return Parse(rawData.AsSpan(), config);
    }

    public List<DeviceData> Parse(ReadOnlySpan<byte> rawData, ProtocolConfig config)
    {
        var result = new List<DeviceData>();

        try
        {
            if (rawData.Length < 18)
            {
                _logger.Warning($"BACnet data too short: {rawData.Length} bytes");
                return result;
            }

            if (rawData[0] != 0x01)
            {
                _logger.Warning($"Invalid BACnet version: {rawData[0]}");
                return result;
            }

            int apduOffset = 16;
            byte apduType = (byte)(rawData[apduOffset] & 0xF0);

            if (apduType == BacnetComplexAck)
            {
                result = ParseComplexAck(rawData.Slice(apduOffset));
            }
            else if (apduType == BacnetUnconfirmedRequest)
            {
                if ((byte)(rawData[apduOffset] & 0xFF) == (byte)(BacnetUnconfirmedRequest | BacnetServiceIAm))
                {
                    result = ParseIAm(rawData.Slice(apduOffset));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to parse BACnet data: {ex.Message}", ex);
        }

        return result;
    }

    public byte[] Pack(DeviceData data, ProtocolConfig config)
    {
        var result = new List<byte>();

        result.Add(0x01);
        result.Add(0x04);
        ushort bvlcLength = (ushort)(4 + GetApduLength(data.DataItems.Count));
        result.AddRange(BitConverter.GetBytes(bvlcLength));

        result.Add(0x01);
        result.Add(0x00);

        result.Add(BacnetConfirmedRequest);
        result.Add(0x04);
        result.Add(0x00);
        result.Add(BacnetServiceReadProperty);

        foreach (var point in data.DataItems)
        {
            var objectId = ParseObjectIdentifier(point.PointName);
            result.AddRange(BitConverter.GetBytes(objectId));
            result.Add((byte)BacnetPropertyId.PresentValue);
        }

        return result.ToArray();
    }

    public byte[] Pack(DeviceCommand command, ProtocolConfig config)
    {
        var result = new List<byte>();

        result.Add(0x01);
        result.Add(0x04);

        result.Add(0x01);
        result.Add(0x00);

        byte serviceType = command.CommandName switch
        {
            "Read" => BacnetServiceReadProperty,
            "Write" => BacnetServiceWriteProperty,
            "Subscribe" => BacnetServiceSubscribeCOV,
            _ => BacnetServiceReadProperty
        };

        result.Add(BacnetConfirmedRequest);
        result.Add(0x04);
        result.Add(0x00);
        result.Add(serviceType);

        if (command.Parameters.TryGetValue("Object", out var obj) && obj is string objectName)
        {
            var objectId = ParseObjectIdentifier(objectName);
            result.AddRange(BitConverter.GetBytes(objectId));
        }

        if (command.Parameters.TryGetValue("Property", out var prop) && prop is int propertyId)
        {
            result.Add((byte)propertyId);
        }
        else
        {
            result.Add((byte)BacnetPropertyId.PresentValue);
        }

        if (serviceType == BacnetServiceWriteProperty && command.Parameters.TryGetValue("Value", out var value))
        {
            result.AddRange(EncodeBacnetValue(value));
        }

        ushort bvlcLength = (ushort)result.Count;
        result[2] = (byte)(bvlcLength >> 8);
        result[3] = (byte)(bvlcLength & 0xFF);

        return result.ToArray();
    }

    public bool Validate(byte[] rawData, ProtocolConfig config)
    {
        if (rawData.Length < 18)
            return false;
        return rawData[0] == 0x01;
    }

    public static byte[] CreateWhoIsRequest(uint lowLimit = 0, uint highLimit = 4194303)
    {
        var result = new List<byte>();

        result.Add(0x01);
        result.Add(0x04);
        result.Add(0x00);
        result.Add(0x0C);

        result.Add(0x01);
        result.Add(0x20);
        result.Add(0xFF);
        result.Add(0xFF);

        result.Add((byte)(BacnetUnconfirmedRequest | 0x08));

        if (lowLimit > 0 || highLimit < 4194303)
        {
            result.AddRange(EncodeBacnetUnsigned(lowLimit));
            result.AddRange(EncodeBacnetUnsigned(highLimit));
        }

        ushort bvlcLength = (ushort)result.Count;
        result[2] = (byte)(bvlcLength >> 8);
        result[3] = (byte)(bvlcLength & 0xFF);

        return result.ToArray();
    }

    public static byte[] CreateReadPropertyRequest(uint deviceInstance, BacnetObjectType objectType, uint objectInstance, BacnetPropertyId propertyId)
    {
        var result = new List<byte>();

        result.Add(0x01);
        result.Add(0x04);
        result.Add(0x00);
        result.Add(0x00);

        result.Add(0x01);
        result.Add(0x00);

        result.Add(BacnetConfirmedRequest);
        result.Add(0x04);
        result.Add(0x00);
        result.Add(BacnetServiceReadProperty);

        uint objectId = ((uint)objectType << 22) | objectInstance;
        result.AddRange(BitConverter.GetBytes(objectId));

        result.Add((byte)propertyId);

        ushort bvlcLength = (ushort)result.Count;
        result[2] = (byte)(bvlcLength >> 8);
        result[3] = (byte)(bvlcLength & 0xFF);

        return result.ToArray();
    }

    public static byte[] CreateWritePropertyRequest(uint deviceInstance, BacnetObjectType objectType, uint objectInstance, BacnetPropertyId propertyId, object value, byte priority = 16)
    {
        var result = new List<byte>();

        result.Add(0x01);
        result.Add(0x04);
        result.Add(0x00);
        result.Add(0x00);

        result.Add(0x01);
        result.Add(0x00);

        result.Add(BacnetConfirmedRequest);
        result.Add(0x04);
        result.Add(0x00);
        result.Add(BacnetServiceWriteProperty);

        uint objectId = ((uint)objectType << 22) | objectInstance;
        result.AddRange(BitConverter.GetBytes(objectId));

        result.Add((byte)propertyId);
        result.AddRange(EncodeBacnetValue(value));

        ushort bvlcLength = (ushort)result.Count;
        result[2] = (byte)(bvlcLength >> 8);
        result[3] = (byte)(bvlcLength & 0xFF);

        return result.ToArray();
    }

    public static byte[] CreateSubscribeCOVRequest(uint subscriberProcessId, uint deviceInstance, BacnetObjectType objectType, uint objectInstance, bool confirmedNotifications, uint lifetime)
    {
        var result = new List<byte>();

        result.Add(0x01);
        result.Add(0x04);
        result.Add(0x00);
        result.Add(0x00);

        result.Add(0x01);
        result.Add(0x00);

        result.Add(BacnetConfirmedRequest);
        result.Add(0x04);
        result.Add(0x00);
        result.Add(BacnetServiceSubscribeCOV);

        result.AddRange(EncodeBacnetUnsigned(subscriberProcessId));

        uint objectId = ((uint)objectType << 22) | objectInstance;
        result.AddRange(BitConverter.GetBytes(objectId));

        result.Add(confirmedNotifications ? (byte)0x01 : (byte)0x00);

        if (lifetime > 0)
        {
            result.AddRange(EncodeBacnetUnsigned(lifetime));
        }

        ushort bvlcLength = (ushort)result.Count;
        result[2] = (byte)(bvlcLength >> 8);
        result[3] = (byte)(bvlcLength & 0xFF);

        return result.ToArray();
    }

    public static uint ParseObjectIdentifier(string objectName)
    {
        var parts = objectName.Split(':');
        if (parts.Length != 2)
            return 0;

        BacnetObjectType objectType = ParseObjectType(parts[0]);
        uint instance = uint.TryParse(parts[1], out var inst) ? inst : 0;

        return ((uint)objectType << 22) | instance;
    }

    public static BacnetObjectType ParseObjectType(string typeString)
    {
        return typeString.ToUpper() switch
        {
            "AI" => BacnetObjectType.AnalogInput,
            "AO" => BacnetObjectType.AnalogOutput,
            "AV" => BacnetObjectType.AnalogValue,
            "BI" => BacnetObjectType.BinaryInput,
            "BO" => BacnetObjectType.BinaryOutput,
            "BV" => BacnetObjectType.BinaryValue,
            "MSI" => BacnetObjectType.MultiStateInput,
            "MSO" => BacnetObjectType.MultiStateOutput,
            "LOOP" => BacnetObjectType.Loop,
            _ => BacnetObjectType.AnalogValue
        };
    }

    private List<DeviceData> ParseComplexAck(ReadOnlySpan<byte> apdu)
    {
        var result = new List<DeviceData>();

        if (apdu.Length < 8)
            return result;

        int offset = 2;

        byte serviceAck = apdu[offset++];
        if (serviceAck != BacnetServiceReadProperty)
            return result;

        uint objectId = BitConverter.ToUInt32(apdu.Slice(offset, 4));
        offset += 4;

        BacnetObjectType objectType = (BacnetObjectType)(objectId >> 22);
        uint objectInstance = objectId & 0x3FFFFF;

        offset += 1;

        var (value, _) = ParseBacnetValue(apdu.Slice(offset));

        var dataPoint = new DataPoint
        {
            PointName = $"{objectType}:{objectInstance}",
            Value = value,
            Quality = 100.0,
            Timestamp = DateTime.UtcNow
        };

        result.Add(new DeviceData
        {
            DeviceId = "bacnet_device",
            CollectTime = DateTime.UtcNow,
            DataItems = new List<DataPoint> { dataPoint }
        });

        return result;
    }

    private List<DeviceData> ParseIAm(ReadOnlySpan<byte> apdu)
    {
        var result = new List<DeviceData>();

        if (apdu.Length < 5)
            return result;

        int offset = 1;

        uint objectId = BitConverter.ToUInt32(apdu.Slice(offset, 4));
        offset += 4;

        BacnetObjectType objectType = (BacnetObjectType)(objectId >> 22);
        uint objectInstance = objectId & 0x3FFFFF;

        var dataPoint = new DataPoint
        {
            PointName = $"Device:{objectInstance}",
            Value = objectInstance,
            Quality = 100.0,
            Timestamp = DateTime.UtcNow
        };

        result.Add(new DeviceData
        {
            DeviceId = $"bacnet_device_{objectInstance}",
            CollectTime = DateTime.UtcNow,
            DataItems = new List<DataPoint> { dataPoint }
        });

        _logger.Info($"BACnet I-Am received: Device {objectInstance}");

        return result;
    }

    private static (object? value, int length) ParseBacnetValue(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            return (null, 0);

        byte tagNumber = (byte)(data[0] >> 4);
        byte lengthValue = (byte)(data[0] & 0x07);

        int offset = 1;

        if (lengthValue == 5)
        {
            if (data.Length < 2)
                return (null, 0);
            lengthValue = data[offset++];
        }

        if (offset + lengthValue > data.Length)
            return (null, 0);

        var valueData = data.Slice(offset, lengthValue);

        object? value = tagNumber switch
        {
            0 => lengthValue > 0 ? BitConverter.ToBoolean(valueData) : false,
            1 => DecodeBacnetUnsigned(valueData),
            2 => DecodeBacnetSigned(valueData),
            3 => lengthValue >= 4 ? BitConverter.ToSingle(valueData) : 0f,
            4 => lengthValue >= 8 ? BitConverter.ToDouble(valueData) : 0d,
            6 => lengthValue > 0 ? Encoding.UTF8.GetString(valueData) : string.Empty,
            _ => null
        };

        return (value, lengthValue);
    }

    private static byte[] EncodeBacnetValue(object value)
    {
        var result = new List<byte>();

        switch (value)
        {
            case bool b:
                result.Add(0x00);
                result.Add((byte)(b ? 1 : 0));
                break;
            case byte bt:
                result.Add(0x11);
                result.Add(bt);
                break;
            case ushort us:
                result.Add(0x12);
                result.AddRange(BitConverter.GetBytes(us));
                break;
            case uint ui:
                result.Add(0x14);
                result.AddRange(BitConverter.GetBytes(ui));
                break;
            case int i:
                result.Add(0x24);
                result.AddRange(BitConverter.GetBytes(i));
                break;
            case float f:
                result.Add(0x34);
                result.AddRange(BitConverter.GetBytes(f));
                break;
            case double d:
                result.Add(0x48);
                result.AddRange(BitConverter.GetBytes(d));
                break;
            case string s:
                result.Add(0x65);
                var bytes = Encoding.UTF8.GetBytes(s);
                result.Add((byte)bytes.Length);
                result.AddRange(bytes);
                break;
            default:
                result.Add(0x00);
                break;
        }

        return result.ToArray();
    }

    private static byte[] EncodeBacnetUnsigned(uint value)
    {
        var result = new List<byte>();
        if (value <= 0xFF)
        {
            result.Add(0x11);
            result.Add((byte)value);
        }
        else if (value <= 0xFFFF)
        {
            result.Add(0x12);
            result.AddRange(BitConverter.GetBytes((ushort)value));
        }
        else
        {
            result.Add(0x14);
            result.AddRange(BitConverter.GetBytes(value));
        }
        return result.ToArray();
    }

    private static uint DecodeBacnetUnsigned(ReadOnlySpan<byte> data)
    {
        return data.Length switch
        {
            1 => data[0],
            2 => BitConverter.ToUInt16(data),
            4 => BitConverter.ToUInt32(data),
            _ => 0
        };
    }

    private static int DecodeBacnetSigned(ReadOnlySpan<byte> data)
    {
        return data.Length switch
        {
            1 => (sbyte)data[0],
            2 => BitConverter.ToInt16(data),
            4 => BitConverter.ToInt32(data),
            _ => 0
        };
    }

    private BacnetConfig GetBacnetConfig(ProtocolConfig config)
    {
        var bacnetConfig = new BacnetConfig();

        if (config.AdditionalSettings.TryGetValue("DeviceId", out var deviceId))
        {
            bacnetConfig.DeviceId = uint.TryParse(deviceId?.ToString(), out var id) ? id : 0;
        }

        if (config.AdditionalSettings.TryGetValue("Port", out var port))
        {
            bacnetConfig.Port = int.TryParse(port?.ToString(), out var p) ? p : 47808;
        }

        return bacnetConfig;
    }

    private static int GetApduLength(int pointCount)
    {
        return 8 + pointCount * 6;
    }
}