using System.Text.Json;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;

namespace Vktun.IoT.Connector.Protocol.Parsers;

public class ModbusRtuParser : IProtocolParser
{
    private readonly ILogger _logger;

    public ProtocolType Type => ProtocolType.ModbusRtu;
    public string Name => "Modbus RTU protocol parser";
    public string Version => "1.0.0";
    public string Description => "Modbus RTU protocol parser";
    public string Vendor => "Vktun";
    public string[] SupportedDeviceModels => new[] { "*" };
    public string Author => "Vktun";
    public ParserStatus Status => ParserStatus.Stable;

    public ModbusRtuParser(ILogger logger)
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
            if (!Validate(rawData.ToArray(), config))
            {
                throw new ArgumentException("Invalid Modbus RTU frame.");
            }

            var modbusConfig = GetModbusConfig(config);
            if (modbusConfig == null)
            {
                throw new InvalidOperationException("Modbus configuration was not found.");
            }

            var response = ParseResponse(rawData);
            if (response.IsError)
            {
                _logger.Error($"Modbus RTU exception response: slave={response.SlaveId}, error={response.ErrorCode}");
                return result;
            }

            var pointData = IsReadFunction(response.FunctionCode)
                ? ParseDataPoints(response.Data, modbusConfig)
                : new List<DataPoint>();

            result.Add(new DeviceData
            {
                DeviceId = $"Modbus_Slave_{response.SlaveId}",
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
            _logger.Error($"Modbus RTU parse failed: {ex.Message}", ex);
        }

        return result;
    }

    public byte[] Pack(DeviceData data, ProtocolConfig config)
    {
        throw new NotImplementedException("Use PackCommand to build Modbus commands.");
    }

    public byte[] Pack(DeviceCommand command, ProtocolConfig config)
    {
        var modbusConfig = GetModbusConfig(config);
        if (modbusConfig == null)
        {
            throw new InvalidOperationException("Modbus configuration was not found.");
        }

        return PackCommand(command, modbusConfig);
    }

    public bool Validate(byte[] rawData, ProtocolConfig config)
    {
        if (rawData.Length < 5)
        {
            return false;
        }

        return CrcCalculator.VerifyCrc16Modbus(rawData, CrcCalculator.Crc16Modbus(rawData, 0, rawData.Length - 2));
    }

    public byte[] PackCommand(DeviceCommand command, ModbusConfig config)
    {
        var pdu = ModbusCommandBuilder.BuildPdu(command);
        return BuildRtuFrame((byte)config.SlaveId, pdu);
    }

    private ModbusConfig? GetModbusConfig(ProtocolConfig config)
    {
        var definition = config.GetDefinition<ModbusConfig>();
        if (definition != null)
        {
            return definition;
        }

        if (!config.ParseRules.TryGetValue("ModbusConfig", out var modbusJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ModbusConfig>(modbusJson);
    }

    private static ModbusResponse ParseResponse(ReadOnlySpan<byte> data)
    {
        var response = new ModbusResponse
        {
            SlaveId = data[0],
            FunctionCode = (ModbusFunctionCode)data[1]
        };

        if ((byte)response.FunctionCode >= 0x80)
        {
            response.IsError = true;
            response.ErrorCode = data[2];
            return response;
        }

        response.Data = IsReadFunction(response.FunctionCode)
            ? ReadByteCountPayload(data)
            : data.Slice(2, data.Length - 4).ToArray();

        return response;
    }

    private List<DataPoint> ParseDataPoints(byte[] data, ModbusConfig config)
    {
        var result = new List<DataPoint>();

        foreach (var point in config.Points)
        {
            try
            {
                var value = ExtractValue(data, point, config);
                var convertedValue = ConvertValue(value, point.Ratio, point.OffsetValue);

                result.Add(new DataPoint
                {
                    PointName = point.PointName,
                    Address = point.Address.ToString(),
                    Value = convertedValue,
                    DataType = point.DataType,
                    Unit = point.Unit,
                    Timestamp = DateTime.Now,
                    IsValid = convertedValue >= point.MinValue && convertedValue <= point.MaxValue
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to parse Modbus point {point.PointName}: {ex.Message}", ex);
            }
        }

        return result;
    }

    private static object ExtractValue(byte[] data, ModbusPointConfig point, ModbusConfig config)
    {
        var byteIndex = point.RegisterType is ModbusRegisterType.Coil or ModbusRegisterType.DiscreteInput
            ? 0
            : point.Address * 2;

        return point.RegisterType switch
        {
            ModbusRegisterType.Coil => ExtractCoil(data, point.Address),
            ModbusRegisterType.DiscreteInput => ExtractCoil(data, point.Address),
            ModbusRegisterType.InputRegister => ExtractRegister(data, byteIndex, point.DataType, config),
            ModbusRegisterType.HoldingRegister => ExtractRegister(data, byteIndex, point.DataType, config),
            _ => throw new NotSupportedException($"Unsupported Modbus register type: {point.RegisterType}")
        };
    }

    private static bool ExtractCoil(byte[] data, ushort address)
    {
        var byteIndex = address / 8;
        var bitIndex = address % 8;

        return byteIndex < data.Length && (data[byteIndex] & (1 << bitIndex)) != 0;
    }

    private static object ExtractRegister(byte[] data, int byteIndex, DataType dataType, ModbusConfig config)
    {
        var byteCount = GetByteCount(dataType);
        if (byteIndex + byteCount > data.Length)
        {
            throw new ArgumentException($"Not enough data. Need {byteCount} bytes, actual {data.Length - byteIndex} bytes.");
        }

        var bytes = new byte[byteCount];
        Array.Copy(data, byteIndex, bytes, 0, byteCount);

        if (config.ByteOrder == ByteOrder.BigEndian)
        {
            if (byteCount == 2)
            {
                Array.Reverse(bytes);
            }
            else if (byteCount == 4)
            {
                if (config.WordOrder == WordOrder.HighWordFirst)
                {
                    Array.Reverse(bytes);
                }
                else
                {
                    bytes = new[] { bytes[2], bytes[3], bytes[0], bytes[1] };
                }
            }
        }

        return dataType switch
        {
            DataType.UInt16 => BitConverter.ToUInt16(bytes, 0),
            DataType.Int16 => BitConverter.ToInt16(bytes, 0),
            DataType.UInt32 => BitConverter.ToUInt32(bytes, 0),
            DataType.Int32 => BitConverter.ToInt32(bytes, 0),
            DataType.Float => BitConverter.ToSingle(bytes, 0),
            _ => BitConverter.ToUInt16(bytes, 0)
        };
    }

    private static int GetByteCount(DataType dataType)
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

    private double ConvertValue(object value, double ratio, double offset)
    {
        try
        {
            return Convert.ToDouble(value) * ratio + offset;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to convert value '{value}' with ratio={ratio}, offset={offset}: {ex.Message}");
            return 0;
        }
    }

    private static byte[] BuildRtuFrame(byte slaveId, byte[] pdu)
    {
        var frame = new List<byte> { slaveId };
        frame.AddRange(pdu);

        var crc = CrcCalculator.Crc16Modbus(frame.ToArray());
        frame.Add((byte)(crc & 0xFF));
        frame.Add((byte)(crc >> 8));

        return frame.ToArray();
    }

    private static bool IsReadFunction(ModbusFunctionCode functionCode)
    {
        return functionCode is ModbusFunctionCode.ReadCoils
            or ModbusFunctionCode.ReadDiscreteInputs
            or ModbusFunctionCode.ReadHoldingRegisters
            or ModbusFunctionCode.ReadInputRegisters;
    }

    private static byte[] ReadByteCountPayload(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5)
        {
            throw new ArgumentException("Modbus RTU read response is too short.");
        }

        var byteCount = data[2];
        if (data.Length < 3 + byteCount + 2)
        {
            throw new ArgumentException("Modbus RTU read response byte count exceeds frame length.");
        }

        return data.Slice(3, byteCount).ToArray();
    }
}
