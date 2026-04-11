using System.Text;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Protocol.Parsers;

/// <summary>
/// CANopen配置
/// </summary>
public class CanopenConfig
{
    public int BaudRate { get; set; } = 250000;
    public byte NodeId { get; set; } = 1;
    public int HeartbeatTimeout { get; set; } = 1000;
    public int SyncWindowLength { get; set; } = 100000;
    public bool EnableSync { get; set; } = true;
    public int SyncPeriod { get; set; } = 100;
    public int MaxPdoCount { get; set; } = 4;
}

/// <summary>
/// CANopen对象字典索引结构
/// </summary>
public class CanopenObjectIndex
{
    public ushort Index { get; set; }
    public byte SubIndex { get; set; }
    public string Name { get; set; } = string.Empty;
    public CanopenDataType DataType { get; set; }
    public CanopenAccessType AccessType { get; set; }
    public object? DefaultValue { get; set; }
    public bool IsPdoMapped { get; set; }
}

/// <summary>
/// CANopen数据类型
/// </summary>
public enum CanopenDataType
{
    Boolean = 0x0001,
    Integer8 = 0x0002,
    Integer16 = 0x0003,
    Integer32 = 0x0004,
    Unsigned8 = 0x0005,
    Unsigned16 = 0x0006,
    Unsigned32 = 0x0007,
    Real32 = 0x0008,
    Real64 = 0x0011,
    VisibleString = 0x0009
}

/// <summary>
/// CANopen访问类型
/// </summary>
public enum CanopenAccessType
{
    ReadOnly,
    WriteOnly,
    ReadWrite,
    Constant
}

/// <summary>
/// CANopen NMT命令
/// </summary>
public enum CanopenNmtCommand
{
    Operational = 0x01,
    Stopped = 0x02,
    PreOperational = 0x80,
    ResetNode = 0x81,
    ResetCommunication = 0x82
}

/// <summary>
/// CANopen协议解析器 - 支持CAN总线设备数据采集
/// </summary>
public class CanopenProtocolParser : IProtocolParser
{
    private readonly ILogger _logger;

    public ProtocolType Type => ProtocolType.Custom;
    public string Name => "CANopen";
    public string Version => "0.0.2";
    public string Description => "CANopen协议解析器";
    public string Vendor => "Vktun";
    public string[] SupportedDeviceModels => new[] { "*" };
    public string Author => "Vktun";
    public ParserStatus Status => ParserStatus.Experimental;

    private const byte NmtInitializing = 0x00;
    private const byte NmtOperational = 0x05;
    private const byte NmtStopped = 0x04;
    private const byte NmtPreOperational = 0x7F;

    public CanopenProtocolParser(ILogger logger)
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
            if (rawData.Length < 5)
            {
                _logger.Warning($"CANopen data too short: {rawData.Length} bytes");
                return result;
            }

            int offset = 0;

            while (offset < rawData.Length)
            {
                if (rawData[offset] == 0xAA || rawData[offset] == 0x00)
                    offset++;

                if (offset + 5 > rawData.Length)
                    break;

                uint canId = BitConverter.ToUInt32(rawData.Slice(offset, 4));
                offset += 4;

                byte dlc = rawData[offset];
                offset += 1;

                if (dlc > 8) dlc = 8;
                if (offset + dlc > rawData.Length) break;

                var canData = rawData.Slice(offset, dlc);
                offset += dlc;

                byte nodeId = (byte)(canId & 0x7F);
                var deviceData = ParseCanMessage(canId, nodeId, canData);
                if (deviceData != null)
                {
                    result.Add(deviceData);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to parse CANopen data: {ex.Message}", ex);
        }

        return result;
    }

    public byte[] Pack(DeviceData data, ProtocolConfig config)
    {
        var canopenConfig = GetCanopenConfig(config);
        var result = new List<byte>();

        foreach (var point in data.DataItems)
        {
            var pdoData = EncodePdoData(point.Value);

            uint canId = 0x180 + (uint)canopenConfig.NodeId;

            result.Add(0xAA);
            result.AddRange(BitConverter.GetBytes(canId));

            byte dlc = (byte)Math.Min(pdoData.Length, 8);
            result.Add(dlc);

            for (int i = 0; i < dlc; i++)
                result.Add(pdoData[i]);

            for (int i = dlc; i < 8; i++)
                result.Add(0);
        }

        return result.ToArray();
    }

    public byte[] Pack(DeviceCommand command, ProtocolConfig config)
    {
        var canopenConfig = GetCanopenConfig(config);
        var result = new List<byte>();

        result.Add(0xAA);

        uint canId;
        byte[] canData;

        switch (command.CommandName)
        {
            case "NMT":
                canId = 0x000;
                canData = CreateNmtCommand(command);
                break;
            case "SDO_Read":
                canId = 0x600 + (uint)canopenConfig.NodeId;
                canData = CreateSdoReadRequest(command);
                break;
            case "SDO_Write":
                canId = 0x600 + (uint)canopenConfig.NodeId;
                canData = CreateSdoWriteRequest(command);
                break;
            case "Sync":
                canId = 0x080;
                canData = Array.Empty<byte>();
                break;
            default:
                canId = 0x600 + (uint)canopenConfig.NodeId;
                canData = CreateSdoReadRequest(command);
                break;
        }

        result.AddRange(BitConverter.GetBytes(canId));

        byte dlc = (byte)Math.Min(canData.Length, 8);
        result.Add(dlc);

        for (int i = 0; i < dlc; i++)
            result.Add(canData[i]);

        for (int i = dlc; i < 8; i++)
            result.Add(0);

        return result.ToArray();
    }

    public bool Validate(byte[] rawData, ProtocolConfig config)
    {
        if (rawData.Length < 5) return false;
        uint canId = BitConverter.ToUInt32(rawData, 0);
        return canId <= 0x7FF || (canId >= 0x800 && canId <= 0x1FFFFFFF);
    }

    public static byte[] CreateNmtCommandFrame(CanopenNmtCommand command, byte nodeId = 0)
    {
        var result = new List<byte>();
        result.Add(0xAA);
        result.AddRange(BitConverter.GetBytes((uint)0x000));
        result.Add(2);
        result.Add((byte)command);
        result.Add(nodeId);
        for (int i = 2; i < 8; i++) result.Add(0);
        return result.ToArray();
    }

    public static byte[] CreateSdoReadRequestFrame(ushort index, byte subIndex, byte nodeId)
    {
        var result = new List<byte>();
        result.Add(0xAA);
        uint canId = 0x600 + (uint)nodeId;
        result.AddRange(BitConverter.GetBytes(canId));
        result.Add(8);
        result.Add(0x40);
        result.Add((byte)(index & 0xFF));
        result.Add((byte)(index >> 8));
        result.Add(subIndex);
        for (int i = 4; i < 8; i++) result.Add(0);
        return result.ToArray();
    }

    public static byte[] CreateSdoWriteRequestFrame(ushort index, byte subIndex, object value, byte nodeId)
    {
        var result = new List<byte>();
        result.Add(0xAA);
        uint canId = 0x600 + (uint)nodeId;
        result.AddRange(BitConverter.GetBytes(canId));
        result.Add(8);

        var dataBytes = EncodeSdoValue(value);
        byte cmdByte = (byte)(0x22);

        result.Add(cmdByte);
        result.Add((byte)(index & 0xFF));
        result.Add((byte)(index >> 8));
        result.Add(subIndex);

        for (int j = 0; j < 4; j++)
            result.Add(j < dataBytes.Length ? dataBytes[j] : (byte)0);

        return result.ToArray();
    }

    public static byte[] CreateSyncFrame()
    {
        var result = new List<byte>();
        result.Add(0xAA);
        result.AddRange(BitConverter.GetBytes((uint)0x080));
        result.Add(0);
        for (int i = 0; i < 8; i++) result.Add(0);
        return result.ToArray();
    }

    public static CanopenObjectIndex ParseObjectIndex(string pointName)
    {
        var parts = pointName.Split(':');
        var result = new CanopenObjectIndex();

        if (parts.Length >= 1 && ushort.TryParse(parts[0], out var idx))
            result.Index = idx;

        if (parts.Length >= 2 && byte.TryParse(parts[1], out var subIdx))
            result.SubIndex = subIdx;

        return InferObjectProperties(result);
    }

    private static CanopenObjectIndex InferObjectProperties(CanopenObjectIndex obj)
    {
        if (obj.Index >= 0x1000 && obj.Index <= 0x1FFF)
        {
            obj.Name = GetDeviceIdentityName(obj.Index);
            obj.DataType = CanopenDataType.Unsigned32;
            obj.AccessType = CanopenAccessType.ReadOnly;
        }
        else if (obj.Index >= 0x2000 && obj.Index <= 0x5FFF)
        {
            obj.Name = $"ManufacturerObject_{obj.Index:X4}";
            obj.DataType = CanopenDataType.Unsigned32;
            obj.AccessType = CanopenAccessType.ReadWrite;
        }
        else if (obj.Index >= 0x6000 && obj.Index <= 0x9FFF)
        {
            obj.Name = GetStandardDeviceName(obj.Index);
            obj.DataType = InferDataTypeFromIndex(obj.Index);
            obj.AccessType = CanopenAccessType.ReadWrite;
        }
        else
        {
            obj.Name = $"Object_{obj.Index:X4}";
            obj.DataType = CanopenDataType.Unsigned32;
            obj.AccessType = CanopenAccessType.ReadWrite;
        }

        return obj;
    }

    private DeviceData? ParseCanMessage(uint canId, byte nodeId, ReadOnlySpan<byte> canData)
    {
        uint functionCode = canId & 0x780;

        if (functionCode == 0x180 || functionCode == 0x280 || functionCode == 0x380 || functionCode == 0x480)
        {
            int pdoNumber = functionCode switch
            {
                0x180 => 1,
                0x280 => 2,
                0x380 => 3,
                0x480 => 4,
                _ => 1
            };
            return ParsePdoMessage(pdoNumber, nodeId, canData);
        }
        else if (functionCode == 0x580)
        {
            return ParseSdoResponse(nodeId, canData);
        }
        else if (canId == 0x000 || functionCode == 0x100)
        {
            return ParseNmtResponse(canData);
        }

        return null;
    }

    private DeviceData ParsePdoMessage(int pdoNumber, byte nodeId, ReadOnlySpan<byte> canData)
    {
        var dataPoints = new List<DataPoint>();

        int offset = 0;
        while (offset < canData.Length)
        {
            var (value, length) = ParsePdoValue(canData.Slice(offset));

            dataPoints.Add(new DataPoint
            {
                PointName = $"PDO{pdoNumber}_Byte{offset}",
                Value = value,
                Quality = 100.0,
                Timestamp = DateTime.UtcNow
            });

            offset += length;
        }

        return new DeviceData
        {
            DeviceId = $"canopen_node_{nodeId}",
            CollectTime = DateTime.UtcNow,
            DataItems = dataPoints
        };
    }

    private DeviceData ParseSdoResponse(byte nodeId, ReadOnlySpan<byte> canData)
    {
        if (canData.Length < 8)
            return new DeviceData { DeviceId = $"canopen_node_{nodeId}" };

        byte commandByte = canData[0];
        ushort index = BitConverter.ToUInt16(canData.Slice(1, 2));
        byte subIndex = canData[3];

        if ((commandByte & 0x80) != 0)
        {
            uint errorCode = BitConverter.ToUInt32(canData.Slice(4, 4));
            _logger.Warning($"SDO abort from node {nodeId}: Error {errorCode:X8}");

            return new DeviceData
            {
                DeviceId = $"canopen_node_{nodeId}",
                CollectTime = DateTime.UtcNow,
                DataItems = new List<DataPoint>
                {
                    new DataPoint { PointName = $"{index:X4}:{subIndex}", Value = errorCode, Quality = 0.0 }
                }
            };
        }

        object? value = null;
        if ((commandByte & 0x02) != 0)
        {
            int dataLength = 4 - ((commandByte >> 2) & 0x03);
            if (dataLength > 0 && dataLength <= 4)
            {
                value = DecodeSdoValue(canData.Slice(4, dataLength));
            }
        }

        return new DeviceData
        {
            DeviceId = $"canopen_node_{nodeId}",
            CollectTime = DateTime.UtcNow,
            DataItems = new List<DataPoint>
            {
                new DataPoint { PointName = $"{index:X4}:{subIndex}", Value = value, Quality = 100.0 }
            }
        };
    }

    private DeviceData ParseNmtResponse(ReadOnlySpan<byte> canData)
    {
        if (canData.Length < 1)
            return new DeviceData { DeviceId = "canopen_nmt" };

        byte status = canData[0];
        string statusName = GetNmtStatusName(status);

        return new DeviceData
        {
            DeviceId = "canopen_nmt",
            CollectTime = DateTime.UtcNow,
            DataItems = new List<DataPoint>
            {
                new DataPoint { PointName = "NMT_Status", Value = statusName, Quality = 100.0 }
            }
        };
    }

    private static (object? value, int length) ParsePdoValue(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1) return (null, 0);

        if (data.Length >= 4) return (BitConverter.ToSingle(data), 4);
        if (data.Length >= 2) return (BitConverter.ToInt16(data), 2);
        return (data[0], 1);
    }

    private static object DecodeSdoValue(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 4) return BitConverter.ToInt32(data);
        if (data.Length >= 2) return BitConverter.ToInt16(data);
        if (data.Length >= 1) return data[0];
        return 0;
    }

    private static byte[] EncodeSdoValue(object? value)
    {
        var result = new List<byte>();

        switch (value)
        {
            case bool b: result.Add((byte)(b ? 1 : 0)); break;
            case byte bt: result.Add(bt); break;
            case sbyte sb: result.Add((byte)sb); break;
            case short s: result.AddRange(BitConverter.GetBytes(s)); break;
            case ushort us: result.AddRange(BitConverter.GetBytes(us)); break;
            case int i: result.AddRange(BitConverter.GetBytes(i)); break;
            case uint ui: result.AddRange(BitConverter.GetBytes(ui)); break;
            case float f: result.AddRange(BitConverter.GetBytes(f)); break;
            case double d: result.AddRange(BitConverter.GetBytes(d)); break;
            default: result.AddRange(BitConverter.GetBytes(0)); break;
        }

        return result.ToArray();
    }

    private static byte[] EncodePdoData(object? value)
    {
        if (value == null) return new byte[8];

        var data = EncodeSdoValue(value);
        var result = new byte[8];

        for (int i = 0; i < Math.Min(data.Length, 8); i++)
            result[i] = data[i];

        return result;
    }

    private static string GetNmtStatusName(byte status)
    {
        return status switch
        {
            NmtInitializing => "Initializing",
            NmtOperational => "Operational",
            NmtStopped => "Stopped",
            NmtPreOperational => "Pre-Operational",
            _ => $"Unknown({status:X2})"
        };
    }

    private static string GetDeviceIdentityName(ushort index)
    {
        return index switch
        {
            0x1000 => "DeviceType",
            0x1001 => "ErrorRegister",
            0x1017 => "ProducerHeartbeatTime",
            _ => $"Identity_{index:X4}"
        };
    }

    private static string GetStandardDeviceName(ushort index)
    {
        ushort deviceType = (ushort)((index / 0x1000) * 0x1000);
        return deviceType switch
        {
            0x6000 => $"GenericIO_{index:X4}",
            0x6400 => $"Drive_{index:X4}",
            _ => $"Device_{index:X4}"
        };
    }

    private static CanopenDataType InferDataTypeFromIndex(ushort index)
    {
        if (index >= 0x6100 && index < 0x6200) return CanopenDataType.Unsigned16;
        if (index >= 0x6200 && index < 0x6300) return CanopenDataType.Unsigned16;
        if (index >= 0x6400 && index < 0x6500) return CanopenDataType.Integer16;
        return CanopenDataType.Unsigned32;
    }

    private byte[] CreateNmtCommand(DeviceCommand command)
    {
        byte cmdByte = 0;

        if (command.Parameters.TryGetValue("Command", out var cmd) && cmd is string cmdStr)
        {
            cmdByte = cmdStr switch
            {
                "Start" => (byte)CanopenNmtCommand.Operational,
                "Stop" => (byte)CanopenNmtCommand.Stopped,
                "PreOperational" => (byte)CanopenNmtCommand.PreOperational,
                "Reset" => (byte)CanopenNmtCommand.ResetNode,
                "ResetComm" => (byte)CanopenNmtCommand.ResetCommunication,
                _ => (byte)CanopenNmtCommand.Operational
            };
        }

        byte nodeId = 0;
        if (command.Parameters.TryGetValue("NodeId", out var node) && node is byte n)
            nodeId = n;

        return new byte[] { cmdByte, nodeId };
    }

    private byte[] CreateSdoReadRequest(DeviceCommand command)
    {
        ushort index = 0;
        byte subIndex = 0;

        if (command.Parameters.TryGetValue("Index", out var idx))
        {
            if (idx is ushort i) index = i;
            else if (idx is string s && ushort.TryParse(s, out var parsed)) index = parsed;
        }

        if (command.Parameters.TryGetValue("SubIndex", out var sub))
        {
            if (sub is byte b) subIndex = b;
            else if (sub is string ss && byte.TryParse(ss, out var parsed)) subIndex = parsed;
        }

        return new byte[] { 0x40, (byte)(index & 0xFF), (byte)(index >> 8), subIndex, 0, 0, 0, 0 };
    }

    private byte[] CreateSdoWriteRequest(DeviceCommand command)
    {
        ushort index = 0;
        byte subIndex = 0;
        object value = 0;

        if (command.Parameters.TryGetValue("Index", out var idx) && idx is ushort i)
            index = i;

        if (command.Parameters.TryGetValue("SubIndex", out var sub) && sub is byte b)
            subIndex = b;

        if (command.Parameters.TryGetValue("Value", out var val))
            value = val;

        var dataBytes = EncodeSdoValue(value);

        var result = new byte[] { 0x22, (byte)(index & 0xFF), (byte)(index >> 8), subIndex, 0, 0, 0, 0 };

        for (int j = 0; j < Math.Min(dataBytes.Length, 4); j++)
            result[4 + j] = dataBytes[j];

        return result;
    }

    private CanopenConfig GetCanopenConfig(ProtocolConfig config)
    {
        var canopenConfig = new CanopenConfig();

        if (config.AdditionalSettings.TryGetValue("NodeId", out var nodeId))
            canopenConfig.NodeId = byte.TryParse(nodeId?.ToString(), out var n) ? n : (byte)1;

        if (config.AdditionalSettings.TryGetValue("BaudRate", out var baudRate))
            canopenConfig.BaudRate = int.TryParse(baudRate?.ToString(), out var br) ? br : 250000;

        return canopenConfig;
    }
}