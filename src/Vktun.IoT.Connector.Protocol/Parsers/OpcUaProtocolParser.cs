using System.Text;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Protocol.Parsers;

/// <summary>
/// OPC UA配置
/// </summary>
public class OpcUaConfig
{
    /// <summary>
    /// 端点URL（如：opc.tcp://localhost:4840）
    /// </summary>
    public string EndpointUrl { get; set; } = string.Empty;

    /// <summary>
    /// 安全模式（None, Sign, SignAndEncrypt）
    /// </summary>
    public string SecurityMode { get; set; } = "None";

    /// <summary>
    /// 安全策略
    /// </summary>
    public string SecurityPolicy { get; set; } = "None";

    /// <summary>
    /// 用户名
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// 密码
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// 订阅间隔（毫秒）
    /// </summary>
    public int PublishingInterval { get; set; } = 1000;

    /// <summary>
    /// 采样间隔（毫秒）
    /// </summary>
    public int SamplingInterval { get; set; } = 1000;

    /// <summary>
    /// 队列大小
    /// </summary>
    public uint QueueSize { get; set; } = 10;

    /// <summary>
    /// 是否启用数据变更通知
    /// </summary>
    public bool EnableDataChangeNotifications { get; set; } = true;
}

/// <summary>
/// OPC UA节点配置
/// </summary>
public class OpcUaNodeConfig
{
    /// <summary>
    /// 节点ID（如：ns=2;s=MyDevice.Temperature）
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 数据类型
    /// </summary>
    public DataType DataType { get; set; } = DataType.Float;

    /// <summary>
    /// 采样间隔（毫秒）
    /// </summary>
    public int SamplingInterval { get; set; } = 1000;

    /// <summary>
    /// 是否订阅
    /// </summary>
    public bool Subscribe { get; set; } = true;

    /// <summary>
    /// 质量阈值（低于此值认为数据无效）
    /// </summary>
    public int QualityThreshold { get; set; } = 192;
}

/// <summary>
/// OPC UA协议解析器 - 支持节点浏览、数据订阅、读写操作
/// </summary>
public class OpcUaProtocolParser : IProtocolParser
{
    private readonly ILogger _logger;

    public ProtocolType Type => ProtocolType.Custom; // OPC UA暂归类为Custom
    public string Name => "OPC UA";
    public string Version => "1.0.0";
    public string Description => "OPC UA协议解析器";
    public string Vendor => "Vktun";
    public string[] SupportedDeviceModels => new[] { "*" };
    public string Author => "Vktun";
    public ParserStatus Status => ParserStatus.Experimental;

    public OpcUaProtocolParser(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 解析OPC UA响应数据
    /// </summary>
    public List<DeviceData> Parse(byte[] rawData, ProtocolConfig config)
    {
        return Parse(rawData.AsSpan(), config);
    }

    /// <summary>
    /// 解析OPC UA响应数据
    /// </summary>
    public List<DeviceData> Parse(ReadOnlySpan<byte> rawData, ProtocolConfig config)
    {
        var result = new List<DeviceData>();

        try
        {
            // OPC UA数据格式：
            // | 节点ID长度(2) | 节点ID | 数据类型(1) | 数据值(N) | 质量(1) | 时间戳(8) |
            if (rawData.Length < 12)
            {
                _logger.Warning($"OPC UA data too short: {rawData.Length} bytes");
                return result;
            }

            int offset = 0;

            while (offset < rawData.Length)
            {
                // 读取节点ID长度
                ushort nodeIdLength = BitConverter.ToUInt16(rawData.Slice(offset, 2));
                offset += 2;

                if (offset + nodeIdLength > rawData.Length)
                    break;

                // 读取节点ID
                string nodeId = Encoding.UTF8.GetString(rawData.Slice(offset, nodeIdLength));
                offset += nodeIdLength;

                // 读取数据类型
                byte dataType = rawData[offset];
                offset += 1;

                // 读取数据值
                var (value, valueLength) = ReadValue(rawData.Slice(offset), (DataType)dataType);
                offset += valueLength;

                // 读取质量
                byte quality = rawData[offset];
                offset += 1;

                // 读取时间戳
                long ticks = BitConverter.ToInt64(rawData.Slice(offset, 8));
                DateTime timestamp = new DateTime(ticks, DateTimeKind.Utc);
                offset += 8;

                // 创建数据点
                var dataPoint = new DataPoint
                {
                    PointName = nodeId,
                    Value = value,
                    Quality = quality >= 192 ? 100.0 : (quality >= 128 ? 0.0 : 50.0),
                    Timestamp = timestamp
                };

                result.Add(new DeviceData
                {
                    DeviceId = "opcua_device",
                    CollectTime = timestamp,
                    DataItems = new List<DataPoint> { dataPoint }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to parse OPC UA data: {ex.Message}", ex);
        }

        return result;
    }

    /// <summary>
    /// 打包读取请求
    /// </summary>
    public byte[] Pack(DeviceData data, ProtocolConfig config)
    {
        // 构建OPC UA读取请求
        var opcuaConfig = GetOpcUaConfig(config);
        var result = new List<byte>();

        foreach (var point in data.DataItems)
        {
            // 节点ID
            var nodeIdBytes = Encoding.UTF8.GetBytes(point.PointName);
            result.AddRange(BitConverter.GetBytes((ushort)nodeIdBytes.Length));
            result.AddRange(nodeIdBytes);

            // 属性ID（0表示值）
            result.Add(0);
        }

        return result.ToArray();
    }

    /// <summary>
    /// 打包写入命令
    /// </summary>
    public byte[] Pack(DeviceCommand command, ProtocolConfig config)
    {
        var result = new List<byte>();

        // 命令类型：0x01=读取，0x02=写入，0x03=订阅
        byte commandType = command.CommandName switch
        {
            "Read" => 0x01,
            "Write" => 0x02,
            "Subscribe" => 0x03,
            _ => 0x01
        };
        result.Add(commandType);

        // 节点数量
        if (command.Parameters.TryGetValue("Nodes", out var nodesObj) && nodesObj is List<string> nodes)
        {
            result.Add((byte)nodes.Count);

            foreach (var node in nodes)
            {
                var nodeBytes = Encoding.UTF8.GetBytes(node);
                result.AddRange(BitConverter.GetBytes((ushort)nodeBytes.Length));
                result.AddRange(nodeBytes);
            }
        }

        // 写入值（如果是写入命令）
        if (commandType == 0x02 && command.Parameters.TryGetValue("Values", out var valuesObj))
        {
            if (valuesObj is List<object> values)
            {
                foreach (var value in values)
                {
                    var valueBytes = EncodeValue(value);
                    result.AddRange(valueBytes);
                }
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// 验证数据
    /// </summary>
    public bool Validate(byte[] rawData, ProtocolConfig config)
    {
        if (rawData.Length < 12)
            return false;

        // 检查最小帧长度
        ushort nodeIdLength = BitConverter.ToUInt16(rawData, 0);
        return rawData.Length >= nodeIdLength + 12;
    }

    /// <summary>
    /// 创建读取请求
    /// </summary>
    public static byte[] CreateReadRequest(IEnumerable<string> nodeIds)
    {
        var result = new List<byte>();

        // 命令类型：读取
        result.Add(0x01);

        // 节点数量
        var nodeList = nodeIds.ToList();
        result.Add((byte)nodeList.Count);

        // 节点ID列表
        foreach (var nodeId in nodeList)
        {
            var nodeBytes = Encoding.UTF8.GetBytes(nodeId);
            result.AddRange(BitConverter.GetBytes((ushort)nodeBytes.Length));
            result.AddRange(nodeBytes);
        }

        return result.ToArray();
    }

    /// <summary>
    /// 创建写入请求
    /// </summary>
    public static byte[] CreateWriteRequest(Dictionary<string, object> values)
    {
        var result = new List<byte>();

        // 命令类型：写入
        result.Add(0x02);

        // 节点数量
        result.Add((byte)values.Count);

        // 节点ID和值
        foreach (var kvp in values)
        {
            var nodeBytes = Encoding.UTF8.GetBytes(kvp.Key);
            result.AddRange(BitConverter.GetBytes((ushort)nodeBytes.Length));
            result.AddRange(nodeBytes);
            result.AddRange(EncodeValue(kvp.Value));
        }

        return result.ToArray();
    }

    /// <summary>
    /// 创建订阅请求
    /// </summary>
    public static byte[] CreateSubscribeRequest(IEnumerable<string> nodeIds, int interval = 1000)
    {
        var result = new List<byte>();

        // 命令类型：订阅
        result.Add(0x03);

        // 订阅间隔
        result.AddRange(BitConverter.GetBytes(interval));

        // 节点数量
        var nodeList = nodeIds.ToList();
        result.Add((byte)nodeList.Count);

        // 节点ID列表
        foreach (var nodeId in nodeList)
        {
            var nodeBytes = Encoding.UTF8.GetBytes(nodeId);
            result.AddRange(BitConverter.GetBytes((ushort)nodeBytes.Length));
            result.AddRange(nodeBytes);
        }

        return result.ToArray();
    }

    /// <summary>
    /// 解析节点ID
    /// </summary>
    public static (ushort namespaceIndex, string identifier, char identifierType) ParseNodeId(string nodeId)
    {
        // 格式：ns=X;s=YYY 或 ns=X;i=NNN 或 ns=X;b=XXX 或 ns=X;g=GUID
        var parts = nodeId.Split(';');
        ushort nsIndex = 0;
        string identifier = string.Empty;
        char idType = 's';

        foreach (var part in parts)
        {
            var keyValue = part.Split('=');
            if (keyValue.Length == 2)
            {
                switch (keyValue[0].Trim().ToLower())
                {
                    case "ns":
                        ushort.TryParse(keyValue[1], out nsIndex);
                        break;
                    case "s":
                        identifier = keyValue[1];
                        idType = 's';
                        break;
                    case "i":
                        identifier = keyValue[1];
                        idType = 'i';
                        break;
                    case "b":
                        identifier = keyValue[1];
                        idType = 'b';
                        break;
                    case "g":
                        identifier = keyValue[1];
                        idType = 'g';
                        break;
                }
            }
        }

        return (nsIndex, identifier, idType);
    }

    private OpcUaConfig GetOpcUaConfig(ProtocolConfig config)
    {
        // 从配置中获取OPC UA特定配置
        var opcuaConfig = new OpcUaConfig();

        if (config.AdditionalSettings.TryGetValue("EndpointUrl", out var endpoint))
        {
            opcuaConfig.EndpointUrl = endpoint?.ToString() ?? string.Empty;
        }

        return opcuaConfig;
    }

    private static (object? value, int length) ReadValue(ReadOnlySpan<byte> data, DataType dataType)
    {
        return dataType switch
        {
            DataType.Bool => (BitConverter.ToBoolean(data), 1),
            DataType.UInt8 => (data[0], 1),
            DataType.Int8 => ((sbyte)data[0], 1),
            DataType.UInt16 => (BitConverter.ToUInt16(data), 2),
            DataType.Int16 => (BitConverter.ToInt16(data), 2),
            DataType.UInt32 => (BitConverter.ToUInt32(data), 4),
            DataType.Int32 => (BitConverter.ToInt32(data), 4),
            DataType.UInt64 => (BitConverter.ToUInt64(data), 8),
            DataType.Int64 => (BitConverter.ToInt64(data), 8),
            DataType.Float => (BitConverter.ToSingle(data), 4),
            DataType.Double => (BitConverter.ToDouble(data), 8),
            DataType.Ascii or DataType.UnicodeString => 
                (Encoding.UTF8.GetString(data.Slice(2, BitConverter.ToUInt16(data))), BitConverter.ToUInt16(data) + 2),
            _ => (null, 0)
        };
    }

    private static byte[] EncodeValue(object value)
    {
        var result = new List<byte>();

        switch (value)
        {
            case bool b:
                result.Add((byte)(b ? 1 : 0));
                break;
            case byte bt:
                result.Add(bt);
                break;
            case sbyte sbt:
                result.Add((byte)sbt);
                break;
            case short s:
                result.AddRange(BitConverter.GetBytes(s));
                break;
            case ushort us:
                result.AddRange(BitConverter.GetBytes(us));
                break;
            case int i:
                result.AddRange(BitConverter.GetBytes(i));
                break;
            case uint ui:
                result.AddRange(BitConverter.GetBytes(ui));
                break;
            case long l:
                result.AddRange(BitConverter.GetBytes(l));
                break;
            case ulong ul:
                result.AddRange(BitConverter.GetBytes(ul));
                break;
            case float f:
                result.AddRange(BitConverter.GetBytes(f));
                break;
            case double d:
                result.AddRange(BitConverter.GetBytes(d));
                break;
            case string s:
                var bytes = Encoding.UTF8.GetBytes(s);
                result.AddRange(BitConverter.GetBytes((ushort)bytes.Length));
                result.AddRange(bytes);
                break;
            default:
                result.Add(0);
                break;
        }

        return result.ToArray();
    }
}