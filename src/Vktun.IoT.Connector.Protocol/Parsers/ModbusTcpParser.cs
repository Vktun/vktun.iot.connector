using System.Text.Json;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;

namespace Vktun.IoT.Connector.Protocol.Parsers;

public class ModbusTcpParser : IProtocolParser
{
    private readonly ILogger _logger;
    private ushort _transactionId;

    public ProtocolType Type => ProtocolType.ModbusTcp;
    public string Name => "Modbus TCP protocol parser";
    public string Version => "1.0.0";
    public string Description => "Modbus TCP protocol parser";
    public string Vendor => "Vktun";
    public string[] SupportedDeviceModels => new[] { "*" };
    public string Author => "Vktun";
    public ParserStatus Status => ParserStatus.Stable;

    public ModbusTcpParser(ILogger logger)
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
                throw new ArgumentException("Invalid Modbus TCP frame.");
            }

            var modbusConfig = GetModbusConfig(config);
            if (modbusConfig == null)
            {
                throw new InvalidOperationException("Modbus configuration was not found.");
            }

            var response = ParseResponse(rawData);
            if (response.IsError)
            {
                _logger.Error($"Modbus TCP exception response: slave={response.SlaveId}, error={response.ErrorCode}");
                return result;
            }

            var pointData = IsReadFunction(response.FunctionCode)
                ? ParseDataPoints(response.Data, modbusConfig)
                : new List<DataPoint>();

            result.Add(new DeviceData
            {
                DeviceId = $"ModbusTCP_Slave_{response.SlaveId}",
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
            _logger.Error($"Modbus TCP parse failed: {ex.Message}", ex);
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
        if (rawData.Length < 8)
        {
            return false;
        }

        var protocolId = (rawData[2] << 8) | rawData[3];
        if (protocolId != 0)
        {
            return false;
        }

        var length = (rawData[4] << 8) | rawData[5];
        return length >= 2 && rawData.Length == length + 6;
    }

    public byte[] PackCommand(DeviceCommand command, ModbusConfig config)
    {
        _transactionId++;
        var pdu = ModbusCommandBuilder.BuildPdu(command);
        return BuildTcpFrame(_transactionId, (byte)config.SlaveId, pdu);
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
            TransactionId = (ushort)((data[0] << 8) | data[1]),
            SlaveId = data[6],
            FunctionCode = (ModbusFunctionCode)data[7]
        };

        if ((byte)response.FunctionCode >= 0x80)
        {
            response.IsError = true;
            response.ErrorCode = data[8];
            return response;
        }

        response.Data = IsReadFunction(response.FunctionCode)
            ? ReadByteCountPayload(data)
            : data.Slice(8, data.Length - 8).ToArray();

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
        var byteCount = ModbusValueConverter.GetByteCount(dataType);
        if (byteIndex + byteCount > data.Length)
        {
            throw new ArgumentException($"Not enough data. Need {byteCount} bytes, actual {data.Length - byteIndex} bytes.");
        }

        return ModbusValueConverter.DecodeRegisterValue(
            data.AsSpan(byteIndex, byteCount),
            dataType,
            config.ByteOrder,
            config.WordOrder);
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

    private static byte[] BuildTcpFrame(ushort transactionId, byte unitId, byte[] pdu)
    {
        var frame = new List<byte>
        {
            (byte)(transactionId >> 8),
            (byte)(transactionId & 0xFF),
            0x00,
            0x00,
            0x00,
            (byte)(pdu.Length + 1),
            unitId
        };
        frame.AddRange(pdu);
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
        if (data.Length < 9)
        {
            throw new ArgumentException("Modbus TCP read response is too short.");
        }

        var byteCount = data[8];
        if (data.Length < 9 + byteCount)
        {
            throw new ArgumentException("Modbus TCP read response byte count exceeds frame length.");
        }

        return data.Slice(9, byteCount).ToArray();
    }
}
