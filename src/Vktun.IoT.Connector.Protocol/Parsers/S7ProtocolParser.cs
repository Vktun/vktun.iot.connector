using System.Buffers.Binary;
using System.Text.Json;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;

namespace Vktun.IoT.Connector.Protocol.Parsers;

public class S7ProtocolParser : IProtocolParser
{
    private readonly ILogger _logger;
    private int _pduReference;

    public ProtocolType Type => ProtocolType.S7;
    public string Name => "西门子S7协议解析器";

    public S7ProtocolParser(ILogger logger)
    {
        _logger = logger;
        _pduReference = 0;
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
            var s7Config = GetS7Config(config);
            if (s7Config == null)
            {
                throw new InvalidOperationException("未找到S7配置");
            }

            var response = ParseS7Response(rawData);
            if (!response.Success)
            {
                _logger.Error($"S7错误响应: 错误码{response.ErrorCode}");
                return result;
            }

            var pointData = ParseDataPoints(response, s7Config);

            result.Add(new DeviceData
            {
                DeviceId = $"S7_{s7Config.CpuType}_{s7Config.Rack}_{s7Config.Slot}",
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
            _logger.Error($"S7协议解析失败: {ex.Message}", ex);
        }

        return result;
    }

    public byte[] Pack(DeviceData data, ProtocolConfig config)
    {
        throw new NotImplementedException("请使用 PackCommand 方法打包命令");
    }

    public byte[] Pack(DeviceCommand command, ProtocolConfig config)
    {
        var s7Config = GetS7Config(config);
        if (s7Config == null)
        {
            throw new InvalidOperationException("未找到S7配置");
        }

        return PackS7Command(command, s7Config);
    }

    public bool Validate(byte[] rawData, ProtocolConfig config)
    {
        if (rawData.Length < 10)
        {
            return false;
        }

        if (rawData[0] != 0x32)
        {
            return false;
        }

        var parameterLength = BinaryPrimitives.ReadUInt16BigEndian(rawData.AsSpan(6, 2));
        var dataLength = BinaryPrimitives.ReadUInt16BigEndian(rawData.AsSpan(8, 2));

        return rawData.Length >= 10 + parameterLength + dataLength;
    }

    public byte[] BuildReadCommand(S7ReadRequest request, S7Config config)
    {
        var pduRef = (ushort)Interlocked.Increment(ref _pduReference);

        var parameterBytes = BuildReadParameter(request);
        var headerBytes = BuildHeader(0x01, pduRef, (ushort)parameterBytes.Length, 0);

        var result = new byte[headerBytes.Length + parameterBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, result, 0, headerBytes.Length);
        Buffer.BlockCopy(parameterBytes, 0, result, headerBytes.Length, parameterBytes.Length);

        return result;
    }

    public byte[] BuildWriteCommand(S7WriteRequest request, S7Config config)
    {
        var pduRef = (ushort)Interlocked.Increment(ref _pduReference);

        var parameterBytes = BuildWriteParameter(request);
        var dataBytes = BuildWriteData(request);

        var headerBytes = BuildHeader(0x01, pduRef, (ushort)parameterBytes.Length, (ushort)dataBytes.Length);

        var result = new byte[headerBytes.Length + parameterBytes.Length + dataBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, result, 0, headerBytes.Length);
        Buffer.BlockCopy(parameterBytes, 0, result, headerBytes.Length, parameterBytes.Length);
        Buffer.BlockCopy(dataBytes, 0, result, headerBytes.Length + parameterBytes.Length, dataBytes.Length);

        return result;
    }

    public S7Response ParseS7Response(ReadOnlySpan<byte> rawData)
    {
        var response = new S7Response();
        response.RawData = rawData.ToArray();

        if (rawData.Length < 10)
        {
            response.Success = false;
            response.ErrorCode = 0x01;
            return response;
        }

        var header = ParseS7Header(rawData);
        if (header.ErrorClass != 0 || header.ErrorCode != 0)
        {
            response.Success = false;
            response.ErrorCode = (byte)((header.ErrorClass << 4) | header.ErrorCode);
            return response;
        }

        if (header.MessageType == 0x02)
        {
            response.Success = ParseS7UserData(rawData.Slice(10, header.ParameterLength + header.DataLength), response);
        }
        else if (header.MessageType == 0x03)
        {
            response.Success = true;
        }
        else
        {
            response.Success = ParseS7ReadWriteResponse(rawData.Slice(10, header.ParameterLength + header.DataLength), response);
        }

        return response;
    }

    private S7Header ParseS7Header(ReadOnlySpan<byte> rawData)
    {
        return new S7Header
        {
            ProtocolId = rawData[0],
            MessageType = rawData[1],
            Reserved = BinaryPrimitives.ReadUInt16BigEndian(rawData.Slice(2, 2)),
            PduReference = BinaryPrimitives.ReadUInt16BigEndian(rawData.Slice(4, 2)),
            ParameterLength = BinaryPrimitives.ReadUInt16BigEndian(rawData.Slice(6, 2)),
            DataLength = BinaryPrimitives.ReadUInt16BigEndian(rawData.Slice(8, 2)),
            ErrorClass = rawData.Length > 10 ? rawData[10] : (byte)0,
            ErrorCode = rawData.Length > 11 ? rawData[11] : (byte)0
        };
    }

    private byte[] BuildHeader(byte messageType, ushort pduReference, ushort parameterLength, ushort dataLength)
    {
        var header = new byte[10];
        header[0] = 0x32;
        header[1] = messageType;
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2, 2), 0x0000);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4, 2), pduReference);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6, 2), parameterLength);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8, 2), dataLength);
        return header;
    }

    private byte[] BuildReadParameter(S7ReadRequest request)
    {
        var items = new List<byte>();

        foreach (var item in request.Items)
        {
            items.Add(0x12);
            items.Add(0x0A);
            items.Add((byte)item.Type);
            items.Add((byte)Math.Max(1, item.Length));
            items.Add((byte)(item.DbNumber >> 8));
            items.Add((byte)item.DbNumber);
            items.Add((byte)item.Area);
            items.Add((byte)((item.StartAddress * 8 + item.BitPosition) >> 16));
            items.Add((byte)(((item.StartAddress * 8 + item.BitPosition) >> 8) & 0xFF));
            items.Add((byte)((item.StartAddress * 8 + item.BitPosition) & 0xFF));
        }

        var parameter = new byte[2 + items.Count];
        parameter[0] = 0x04;
        parameter[1] = (byte)request.Items.Count;
        Buffer.BlockCopy(items.ToArray(), 0, parameter, 2, items.Count);

        return parameter;
    }

    private byte[] BuildWriteParameter(S7WriteRequest request)
    {
        var items = new List<byte>();

        foreach (var item in request.Items)
        {
            items.Add(0x12);
            items.Add(0x0A);
            items.Add((byte)item.Type);
            items.Add((byte)Math.Max(1, item.Length));
            items.Add((byte)(item.DbNumber >> 8));
            items.Add((byte)item.DbNumber);
            items.Add((byte)item.Area);
            items.Add((byte)((item.StartAddress * 8 + item.BitPosition) >> 16));
            items.Add((byte)(((item.StartAddress * 8 + item.BitPosition) >> 8) & 0xFF));
            items.Add((byte)((item.StartAddress * 8 + item.BitPosition) & 0xFF));
        }

        var parameter = new byte[2 + items.Count];
        parameter[0] = 0x05;
        parameter[1] = (byte)request.Items.Count;
        Buffer.BlockCopy(items.ToArray(), 0, parameter, 2, items.Count);

        return parameter;
    }

    private byte[] BuildWriteData(S7WriteRequest request)
    {
        var data = new List<byte>();

        foreach (var item in request.Items)
        {
            if (item.Data == null) continue;

            data.Add(0xFF);
            data.Add((byte)item.Type);
            data.Add((byte)(item.Data.Length >> 8));
            data.Add((byte)(item.Data.Length & 0xFF));

            data.AddRange(item.Data);

            if (item.Data.Length % 2 != 0)
            {
                data.Add(0x00);
            }
        }

        return data.ToArray();
    }

    private bool ParseS7ReadWriteResponse(ReadOnlySpan<byte> data, S7Response response)
    {
        if (data.Length < 2)
        {
            return false;
        }

        var functionCode = data[0];
        var itemCount = data[1];

        var dataOffset = 2;

        for (int i = 0; i < itemCount && dataOffset < data.Length; i++)
        {
            if (dataOffset + 4 > data.Length) break;

            var returnCode = data[dataOffset];
            var transportSize = data[dataOffset + 1];
            var dataLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(dataOffset + 2, 2));

            dataOffset += 4;

            if (dataOffset + dataLength > data.Length) break;

            var itemData = data.Slice(dataOffset, dataLength).ToArray();
            response.Items.Add(new S7DataItem
            {
                Data = itemData,
                Type = (S7DataItemType)(transportSize & 0x0F)
            });

            dataOffset += dataLength;
            if (dataLength % 2 != 0)
            {
                dataOffset++;
            }
        }

        return true;
    }

    private bool ParseS7UserData(ReadOnlySpan<byte> data, S7Response response)
    {
        response.Success = data.Length >= 8 && data[0] == 0x00 && data[1] == 0x00;
        return response.Success;
    }

    private List<DataPoint> ParseDataPoints(S7Response response, S7Config config)
    {
        var result = new List<DataPoint>();

        for (int i = 0; i < Math.Min(response.Items.Count, config.Points.Count); i++)
        {
            var item = response.Items[i];
            var point = config.Points[i];

            if (item.Data == null) continue;

            var value = ParseValue(item.Data, point.DataType, config.ByteOrder, config.WordOrder);
            var adjustedValue = value * point.Ratio + point.OffsetValue;

            result.Add(new DataPoint
            {
                PointName = point.PointName,
                Value = adjustedValue,
                Unit = point.Unit,
                DataType = point.DataType,
                Timestamp = DateTime.Now,
                IsValid = true
            });
        }

        return result;
    }

    private double ParseValue(byte[] data, DataType dataType, ByteOrder byteOrder, WordOrder wordOrder)
    {
        try
        {
            return ConvertToDouble(data, 0, dataType, byteOrder, wordOrder);
        }
        catch (Exception ex)
        {
            _logger.Warning($"数据转换失败: {ex.Message}");
            return 0;
        }
    }

    private double ConvertToDouble(byte[] data, int startIndex, DataType dataType, ByteOrder byteOrder, WordOrder wordOrder)
    {
        var byteCount = GetByteCount(dataType);

        if (startIndex + byteCount > data.Length)
        {
            throw new ArgumentException($"数据长度不足: 需要{byteCount}字节, 实际{data.Length - startIndex}字节");
        }

        var bytes = new byte[byteCount];
        Array.Copy(data, startIndex, bytes, 0, byteCount);

        if (byteOrder == ByteOrder.BigEndian)
        {
            if (byteCount == 2)
            {
                Array.Reverse(bytes);
            }
            else if (byteCount == 4)
            {
                if (wordOrder == WordOrder.HighWordFirst)
                {
                    Array.Reverse(bytes);
                }
                else
                {
                    var temp = new byte[4];
                    temp[0] = bytes[2];
                    temp[1] = bytes[3];
                    temp[2] = bytes[0];
                    temp[3] = bytes[1];
                    bytes = temp;
                }
            }
            else if (byteCount == 8)
            {
                Array.Reverse(bytes);
            }
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
            _ => BitConverter.ToUInt16(bytes, 0)
        };
    }

    private int GetByteCount(DataType dataType)
    {
        return dataType switch
        {
            DataType.UInt8 or DataType.Int8 => 1,
            DataType.UInt16 or DataType.Int16 => 2,
            DataType.UInt32 or DataType.Int32 or DataType.Float => 4,
            DataType.UInt64 or DataType.Int64 or DataType.Double => 8,
            _ => 2
        };
    }

    private byte[] PackS7Command(DeviceCommand command, S7Config config)
    {
        throw new NotImplementedException("S7命令打包需要使用BuildReadCommand或BuildWriteCommand方法");
    }

    private S7Config? GetS7Config(ProtocolConfig config)
    {
        try
        {
            var s7Config = new S7Config
            {
                ProtocolId = config.ProtocolId,
                ProtocolName = config.ProtocolName,
                Description = config.Description,
                ProtocolType = config.ProtocolType
            };

            if (config.ParseRules.TryGetValue("CpuType", out var cpuTypeStr))
            {
                if (Enum.TryParse<S7CpuType>(cpuTypeStr?.ToString(), out var cpuType))
                {
                    s7Config.CpuType = cpuType;
                }
            }

            if (config.ParseRules.TryGetValue("Rack", out var rackObj))
            {
                if (int.TryParse(rackObj?.ToString(), out var rack))
                {
                    s7Config.Rack = rack;
                }
            }

            if (config.ParseRules.TryGetValue("Slot", out var slotObj))
            {
                if (int.TryParse(slotObj?.ToString(), out var slot))
                {
                    s7Config.Slot = slot;
                }
            }

            if (config.ParseRules.TryGetValue("Port", out var portObj))
            {
                if (int.TryParse(portObj?.ToString(), out var port))
                {
                    s7Config.Port = port;
                }
            }

            if (config.ParseRules.TryGetValue("PduSize", out var pduSizeObj))
            {
                if (int.TryParse(pduSizeObj?.ToString(), out var pduSize))
                {
                    s7Config.PduSize = pduSize;
                }
            }

            if (config.ParseRules.TryGetValue("Points", out var pointsJson))
            {
                var points = JsonSerializer.Deserialize<List<S7PointConfig>>(pointsJson?.ToString() ?? "[]",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (points != null)
                {
                    s7Config.Points = points;
                }
            }

            return s7Config;
        }
        catch (Exception ex)
        {
            _logger.Error($"解析S7配置失败: {ex.Message}", ex);
            return null;
        }
    }
}
