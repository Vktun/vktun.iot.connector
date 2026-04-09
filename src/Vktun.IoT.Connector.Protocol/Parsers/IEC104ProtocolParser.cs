using System.Buffers.Binary;
using System.Text.Json;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Protocol.Parsers;

public class IEC104ProtocolParser : IProtocolParser
{
    private readonly ILogger _logger;
    private int _sendSeqNumber;
    private int _receiveSeqNumber;

    public ProtocolType Type => ProtocolType.IEC104;
    public string Name => "IEC104电力协议解析器";

    public IEC104ProtocolParser(ILogger logger)
    {
        _logger = logger;
        _sendSeqNumber = 0;
        _receiveSeqNumber = 0;
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
            var iec104Config = GetIEC104Config(config);
            if (iec104Config == null)
            {
                throw new InvalidOperationException("未找到IEC104配置");
            }

            var response = ParseIEC104Response(rawData);
            if (!response.Success)
            {
                _logger.Error($"IEC104错误响应: {response.ErrorMessage}");
                return result;
            }

            if (response.Apdu?.Asdu != null)
            {
                var pointData = ParseDataPoints(response.Apdu.Asdu, iec104Config);

                result.Add(new DeviceData
                {
                    DeviceId = $"IEC104_CA_{iec104Config.CommonAddress}",
                    ChannelId = config.ChannelId,
                    ProtocolType = Type,
                    CollectTime = DateTime.Now,
                    DataItems = pointData,
                    RawData = rawData.ToArray(),
                    IsValid = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"IEC104协议解析失败: {ex.Message}", ex);
        }

        return result;
    }

    public byte[] Pack(DeviceData data, ProtocolConfig config)
    {
        throw new NotImplementedException("请使用 PackCommand 方法打包命令");
    }

    public byte[] Pack(DeviceCommand command, ProtocolConfig config)
    {
        var iec104Config = GetIEC104Config(config);
        if (iec104Config == null)
        {
            throw new InvalidOperationException("未找到IEC104配置");
        }

        return PackIEC104Command(command, iec104Config);
    }

    public bool Validate(byte[] rawData, ProtocolConfig config)
    {
        if (rawData.Length < 6)
        {
            return false;
        }

        if (rawData[0] != 0x68)
        {
            return false;
        }

        var length = rawData[1];
        return rawData.Length >= length + 2;
    }

    public byte[] BuildStartDtCommand()
    {
        return new byte[] { 0x68, 0x04, 0x07, 0x00, 0x00, 0x00 };
    }

    public byte[] BuildInterrogationCommand(int commonAddress, byte qualifier)
    {
        var asduData = new List<byte>
        {
            0x64,
            0x01,
            0x06,
            0x00,
            (byte)(commonAddress & 0xFF),
            (byte)(commonAddress >> 8),
            0x00,
            0x00,
            qualifier
        };

        return BuildApdu(asduData.ToArray());
    }

    public byte[] BuildClockSyncCommand(int commonAddress, DateTime time)
    {
        var timeBytes = EncodeCP56Time2a(time);
        var asduData = new List<byte>
        {
            103,
            0x01,
            0x06,
            0x00,
            (byte)(commonAddress & 0xFF),
            (byte)(commonAddress >> 8),
            0x00,
            0x00,
            0x00
        };
        asduData.AddRange(timeBytes);

        return BuildApdu(asduData.ToArray());
    }

    public byte[] BuildControlCommand(IEC104ControlCommand command, int commonAddress)
    {
        var asduData = new List<byte>
        {
            (byte)command.TypeId,
            0x01,
            (byte)(command.Select ? 0x06 : 0x07),
            0x00,
            (byte)(commonAddress & 0xFF),
            (byte)(commonAddress >> 8),
            (byte)(command.IoAddress & 0xFF),
            (byte)((command.IoAddress >> 8) & 0xFF),
            (byte)(command.IoAddress >> 16)
        };

        switch (command.TypeId)
        {
            case IEC104TypeIdentification.C_SC_NA_1:
                asduData.Add((byte)(((command.Value?.ToString() == "1" ? 1 : 0) << 7) | command.Qualifier));
                break;
            case IEC104TypeIdentification.C_DC_NA_1:
                asduData.Add((byte)(((int.Parse(command.Value?.ToString() ?? "0") & 0x03) << 6) | command.Qualifier));
                break;
            case IEC104TypeIdentification.C_SE_NA_1:
                var shortVal = (short)(float.Parse(command.Value?.ToString() ?? "0") * 32768);
                asduData.Add((byte)(shortVal & 0xFF));
                asduData.Add((byte)(shortVal >> 8));
                asduData.Add(command.Qualifier);
                break;
        }

        return BuildApdu(asduData.ToArray());
    }

    private byte[] BuildApdu(byte[]? asduData = null)
    {
        var apduLength = asduData != null ? (byte)(4 + asduData.Length) : (byte)4;
        var sendSeq = (ushort)Interlocked.Increment(ref _sendSeqNumber);
        var receiveSeq = (ushort)_receiveSeqNumber;
        var apdu = new List<byte>
        {
            0x68,
            apduLength,
            (byte)((sendSeq << 1) & 0xFE),
            (byte)((sendSeq >> 7) & 0xFF),
            (byte)((receiveSeq << 1) & 0xFE),
            (byte)((receiveSeq >> 7) & 0xFF)
        };

        if (asduData != null)
        {
            apdu.AddRange(asduData);
        }

        return apdu.ToArray();
    }

    public IEC104Response ParseIEC104Response(ReadOnlySpan<byte> rawData)
    {
        var response = new IEC104Response
        {
            RawData = rawData.ToArray()
        };

        try
        {
            if (rawData.Length < 6)
            {
                response.Success = false;
                response.ErrorMessage = "数据长度不足";
                return response;
            }

            if (rawData[0] != 0x68)
            {
                response.Success = false;
                response.ErrorMessage = "无效的起始字节";
                return response;
            }

            var apdu = ParseApdu(rawData);
            response.Apdu = apdu;
            response.Success = true;
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.ErrorMessage = ex.Message;
        }

        return response;
    }

    private IEC104Apdu ParseApdu(ReadOnlySpan<byte> data)
    {
        var apdu = new IEC104Apdu
        {
            StartByte = data[0],
            Length = data[1],
            SendSeqNumber = (ushort)((data[2] >> 1) | (data[3] << 7)),
            ReceiveSeqNumber = (ushort)((data[4] >> 1) | (data[5] << 7))
        };

        if (apdu.Length > 4 && data.Length > 6)
        {
            apdu.Asdu = ParseAsdu(data.Slice(6, apdu.Length - 4));
        }

        return apdu;
    }

    private IEC104Asdu ParseAsdu(ReadOnlySpan<byte> data)
    {
        var asdu = new IEC104Asdu
        {
            TypeId = (IEC104TypeIdentification)data[0],
            VariableStructure = data[1],
            CauseOfTransmission = (byte)(data[2] & 0x3F),
            IsNegative = (data[2] & 0x40) != 0,
            IsTest = (data[2] & 0x80) != 0,
            OriginatorAddress = data[3],
            CommonAddress = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(4, 2))
        };

        var isSequence = (asdu.VariableStructure & 0x80) != 0;
        var objectCount = asdu.VariableStructure & 0x7F;
        var infoObjectIndex = 7;

        for (int i = 0; i < objectCount && infoObjectIndex < data.Length; i++)
        {
            var io = ParseInformationObject(data.Slice(infoObjectIndex), asdu.TypeId, isSequence, i);
            asdu.InformationObjects.Add(io);

            if (!isSequence)
            {
                infoObjectIndex += GetInformationObjectLength(asdu.TypeId);
            }
            else
            {
                infoObjectIndex += 3;
                if (i == 0)
                {
                    infoObjectIndex += GetInformationElementLength(asdu.TypeId);
                }
            }
        }

        return asdu;
    }

    private IEC104InformationObject ParseInformationObject(ReadOnlySpan<byte> data, IEC104TypeIdentification typeId, bool isSequence, int index)
    {
        var io = new IEC104InformationObject();

        if (data.Length >= 3)
        {
            io.IoAddress = (uint)(data[0] | (data[1] << 8) | (data[2] << 16));
        }

        var elementData = isSequence && index > 0 ? data : data.Slice(3);
        ParseInformationElement(elementData, typeId, io);

        return io;
    }

    private void ParseInformationElement(ReadOnlySpan<byte> data, IEC104TypeIdentification typeId, IEC104InformationObject io)
    {
        switch (typeId)
        {
            case IEC104TypeIdentification.M_SP_NA_1:
            case IEC104TypeIdentification.M_SP_TA_1:
            case IEC104TypeIdentification.M_SP_TB_1:
                if (data.Length >= 1)
                {
                    io.Value = (data[0] & 0x01) == 1;
                    io.Quality = (byte)(data[0] >> 1);
                }
                if (typeId != IEC104TypeIdentification.M_SP_NA_1 && data.Length >= 8)
                {
                    io.TimeStamp = DecodeCP56Time2a(data.Slice(1));
                }
                break;

            case IEC104TypeIdentification.M_DP_NA_1:
            case IEC104TypeIdentification.M_DP_TA_1:
            case IEC104TypeIdentification.M_DP_TB_1:
                if (data.Length >= 1)
                {
                    io.Value = (IEC104DoublePointValue)(data[0] & 0x03);
                    io.Quality = (byte)(data[0] >> 2);
                }
                if (typeId != IEC104TypeIdentification.M_DP_NA_1 && data.Length >= 8)
                {
                    io.TimeStamp = DecodeCP56Time2a(data.Slice(1));
                }
                break;

            case IEC104TypeIdentification.M_ME_NA_1:
            case IEC104TypeIdentification.M_ME_TA_1:
            case IEC104TypeIdentification.M_ME_TD_1:
                if (data.Length >= 3)
                {
                    var normalizedVal = BinaryPrimitives.ReadInt16LittleEndian(data);
                    io.Value = normalizedVal / 32768.0;
                    io.Quality = data[2];
                }
                if (typeId != IEC104TypeIdentification.M_ME_NA_1 && data.Length >= 10)
                {
                    io.TimeStamp = DecodeCP56Time2a(data.Slice(3));
                }
                break;

            case IEC104TypeIdentification.M_ME_NB_1:
            case IEC104TypeIdentification.M_ME_TB_1:
            case IEC104TypeIdentification.M_ME_TE_1:
                if (data.Length >= 3)
                {
                    io.Value = BinaryPrimitives.ReadInt16LittleEndian(data);
                    io.Quality = data[2];
                }
                if (typeId != IEC104TypeIdentification.M_ME_NB_1 && data.Length >= 10)
                {
                    io.TimeStamp = DecodeCP56Time2a(data.Slice(3));
                }
                break;

            case IEC104TypeIdentification.M_ME_NC_1:
            case IEC104TypeIdentification.M_ME_TC_1:
            case IEC104TypeIdentification.M_ME_TF_1:
                if (data.Length >= 5)
                {
                    io.Value = BinaryPrimitives.ReadSingleLittleEndian(data);
                    io.Quality = data[4];
                }
                if (typeId != IEC104TypeIdentification.M_ME_NC_1 && data.Length >= 12)
                {
                    io.TimeStamp = DecodeCP56Time2a(data.Slice(5));
                }
                break;

            case IEC104TypeIdentification.M_IT_NA_1:
            case IEC104TypeIdentification.M_IT_TA_1:
            case IEC104TypeIdentification.M_IT_TB_1:
                if (data.Length >= 5)
                {
                    io.Value = BinaryPrimitives.ReadUInt32LittleEndian(data);
                    io.Quality = data[4];
                }
                if (typeId != IEC104TypeIdentification.M_IT_NA_1 && data.Length >= 12)
                {
                    io.TimeStamp = DecodeCP56Time2a(data.Slice(5));
                }
                break;
        }
    }

    private DateTime? DecodeCP56Time2a(ReadOnlySpan<byte> data)
    {
        if (data.Length < 7) return null;

        try
        {
            int milliseconds = (data[0] & 0xFF) | ((data[1] & 0xFF) << 8);
            int seconds = milliseconds / 1000;
            milliseconds = milliseconds % 1000;

            int minute = data[2] & 0x3F;
            int hour = data[3] & 0x1F;
            int day = data[4] & 0x1F;
            int month = data[5] & 0x0F;
            int year = (data[6] & 0x7F) + 2000;

            return new DateTime(year, month, day, hour, minute, seconds, milliseconds);
        }
        catch
        {
            return null;
        }
    }

    private byte[] EncodeCP56Time2a(DateTime time)
    {
        var milliseconds = (ushort)(time.Millisecond + time.Second * 1000);
        var result = new byte[7];

        result[0] = (byte)(milliseconds & 0xFF);
        result[1] = (byte)((milliseconds >> 8) & 0xFF);
        result[2] = (byte)(time.Minute & 0x3F);
        result[3] = (byte)(time.Hour & 0x1F);
        result[4] = (byte)(time.Day & 0x1F);
        result[5] = (byte)(time.Month & 0x0F);
        result[6] = (byte)((time.Year - 2000) & 0x7F);

        return result;
    }

    private int GetInformationObjectLength(IEC104TypeIdentification typeId)
    {
        return 3 + GetInformationElementLength(typeId);
    }

    private int GetInformationElementLength(IEC104TypeIdentification typeId)
    {
        return typeId switch
        {
            IEC104TypeIdentification.M_SP_NA_1 => 1,
            IEC104TypeIdentification.M_SP_TA_1 or IEC104TypeIdentification.M_SP_TB_1 => 8,
            IEC104TypeIdentification.M_DP_NA_1 => 1,
            IEC104TypeIdentification.M_DP_TA_1 or IEC104TypeIdentification.M_DP_TB_1 => 8,
            IEC104TypeIdentification.M_ME_NA_1 or IEC104TypeIdentification.M_ME_NB_1 => 3,
            IEC104TypeIdentification.M_ME_TA_1 or IEC104TypeIdentification.M_ME_TB_1 or
            IEC104TypeIdentification.M_ME_TD_1 or IEC104TypeIdentification.M_ME_TE_1 => 10,
            IEC104TypeIdentification.M_ME_NC_1 => 5,
            IEC104TypeIdentification.M_ME_TC_1 or IEC104TypeIdentification.M_ME_TF_1 => 12,
            IEC104TypeIdentification.M_IT_NA_1 => 5,
            IEC104TypeIdentification.M_IT_TA_1 or IEC104TypeIdentification.M_IT_TB_1 => 12,
            _ => 1
        };
    }

    private List<DataPoint> ParseDataPoints(IEC104Asdu asdu, IEC104Config config)
    {
        var result = new List<DataPoint>();

        foreach (var io in asdu.InformationObjects)
        {
            var point = config.Points.FirstOrDefault(p => p.IoAddress == io.IoAddress);
            if (point == null) continue;

            var value = ConvertValue(io.Value, point.Ratio, point.OffsetValue);

            result.Add(new DataPoint
            {
                PointName = point.PointName,
                Address = io.IoAddress.ToString(),
                Value = value,
                DataType = point.DataType,
                Unit = point.Unit,
                Timestamp = io.TimeStamp ?? DateTime.Now,
                IsValid = (io.Quality & 0x10) == 0
            });
        }

        return result;
    }

    private double ConvertValue(object? value, double ratio, double offset)
    {
        try
        {
            if (value == null) return 0;
            var numericValue = Convert.ToDouble(value);
            return numericValue * ratio + offset;
        }
        catch (Exception ex)
        {
            _logger.Warning($"值转换失败: {ex.Message}");
            return 0;
        }
    }

    private byte[] PackIEC104Command(DeviceCommand command, IEC104Config config)
    {
        if (command.Parameters.TryGetValue("CommandType", out var cmdType))
        {
            switch (cmdType?.ToString())
            {
                case "StartDT":
                    return BuildStartDtCommand();
                case "Interrogation":
                    var qualifier = command.Parameters.TryGetValue("Qualifier", out var q) ? byte.Parse(q?.ToString() ?? "20") : (byte)20;
                    return BuildInterrogationCommand(config.CommonAddress, qualifier);
                case "ClockSync":
                    var time = command.Parameters.TryGetValue("Time", out var t) ? DateTime.Parse(t?.ToString() ?? DateTime.Now.ToString()) : DateTime.Now;
                    return BuildClockSyncCommand(config.CommonAddress, time);
                case "Control":
                    if (command.Parameters.TryGetValue("ControlCommand", out var ctrlCmd) && ctrlCmd is IEC104ControlCommand iecCmd)
                    {
                        return BuildControlCommand(iecCmd, config.CommonAddress);
                    }
                    break;
            }
        }
        return Array.Empty<byte>();
    }

    private IEC104Config? GetIEC104Config(ProtocolConfig config)
    {
        try
        {
            var iec104Config = new IEC104Config
            {
                ProtocolId = config.ProtocolId,
                ProtocolName = config.ProtocolName,
                Description = config.Description,
                ProtocolType = config.ProtocolType
            };

            if (config.ParseRules.TryGetValue("CommonAddress", out var caObj))
            {
                if (int.TryParse(caObj?.ToString(), out var ca))
                {
                    iec104Config.CommonAddress = ca;
                }
            }

            if (config.ParseRules.TryGetValue("Port", out var portObj))
            {
                if (int.TryParse(portObj?.ToString(), out var port))
                {
                    iec104Config.Port = port;
                }
            }

            if (config.ParseRules.TryGetValue("Points", out var pointsJson))
            {
                var points = JsonSerializer.Deserialize<List<IEC104PointConfig>>(pointsJson?.ToString() ?? "[]",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (points != null)
                {
                    iec104Config.Points = points;
                }
            }

            return iec104Config;
        }
        catch (Exception ex)
        {
            _logger.Error($"解析IEC104配置失败: {ex.Message}", ex);
            return null;
        }
    }
}
