using System.Text.Json;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Protocol.Parsers;

public class ModbusTcpParser : IProtocolParser
{
    private readonly ILogger _logger;
    private ushort _transactionId;

    public ProtocolType Type => ProtocolType.ModbusTcp;
    public string Name => "Modbus TCP协议解析器";

    public ModbusTcpParser(ILogger logger)
    {
        _logger = logger;
        _transactionId = 0;
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
            var modbusConfig = GetModbusConfig(config);
            if (modbusConfig == null)
            {
                throw new InvalidOperationException("未找到Modbus配置");
            }
            
            var response = ParseResponse(rawData, modbusConfig);
            if (response.IsError)
            {
                _logger.Error($"Modbus TCP错误响应: 从站{response.SlaveId}, 错误码{response.ErrorCode}");
                return result;
            }
            
            var pointData = ParseDataPoints(response.Data, modbusConfig);
            
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
            _logger.Error($"Modbus TCP解析失败: {ex.Message}", ex);
        }
        
        return result;
    }

    public byte[] Pack(DeviceData data, ProtocolConfig config)
    {
        throw new NotImplementedException("请使用 PackCommand 方法打包命令");
    }

    public byte[] Pack(DeviceCommand command, ProtocolConfig config)
    {
        var modbusConfig = GetModbusConfig(config);
        if (modbusConfig == null)
        {
            throw new InvalidOperationException("未找到Modbus配置");
        }
        
        return PackCommand(command, modbusConfig);
    }

    public bool Validate(byte[] rawData, ProtocolConfig config)
    {
        if (rawData.Length < 9)
        {
            return false;
        }
        
        var length = (rawData[4] << 8) | rawData[5];
        return rawData.Length >= length + 6;
    }

    private ModbusConfig? GetModbusConfig(ProtocolConfig config)
    {
        if (!config.ParseRules.TryGetValue("ModbusConfig", out var modbusJson))
        {
            return null;
        }
        
        return JsonSerializer.Deserialize<ModbusConfig>(modbusJson);
    }

    private ModbusResponse ParseResponse(ReadOnlySpan<byte> data, ModbusConfig config)
    {
        if (data.Length < 9)
        {
            throw new ArgumentException("响应数据长度不足");
        }
        
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
        
        var byteCount = data[8];
        response.Data = data.Slice(9, byteCount).ToArray();
        
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
                _logger.Error($"解析点位 {point.PointName} 失败: {ex.Message}", ex);
            }
        }
        
        return result;
    }

    private object ExtractValue(byte[] data, ModbusPointConfig point, ModbusConfig config)
    {
        var byteIndex = point.Address * (point.RegisterType == ModbusRegisterType.Coil || 
                                         point.RegisterType == ModbusRegisterType.DiscreteInput ? 0 : 2);
        
        return point.RegisterType switch
        {
            ModbusRegisterType.Coil => ExtractCoil(data, point.Address),
            ModbusRegisterType.DiscreteInput => ExtractDiscreteInput(data, point.Address),
            ModbusRegisterType.InputRegister => ExtractRegister(data, byteIndex, point.DataType, config),
            ModbusRegisterType.HoldingRegister => ExtractRegister(data, byteIndex, point.DataType, config),
            _ => throw new NotSupportedException($"不支持的寄存器类型: {point.RegisterType}")
        };
    }

    private bool ExtractCoil(byte[] data, ushort address)
    {
        var byteIndex = address / 8;
        var bitIndex = address % 8;
        
        if (byteIndex >= data.Length)
        {
            return false;
        }
        
        return (data[byteIndex] & (1 << bitIndex)) != 0;
    }

    private bool ExtractDiscreteInput(byte[] data, ushort address)
    {
        return ExtractCoil(data, address);
    }

    private object ExtractRegister(byte[] data, int byteIndex, DataType dataType, ModbusConfig config)
    {
        var byteCount = GetByteCount(dataType);
        
        if (byteIndex + byteCount > data.Length)
        {
            throw new ArgumentException($"数据长度不足: 需要{byteCount}字节, 实际{data.Length - byteIndex}字节");
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
                    var temp = new byte[4];
                    temp[0] = bytes[2];
                    temp[1] = bytes[3];
                    temp[2] = bytes[0];
                    temp[3] = bytes[1];
                    bytes = temp;
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

    public byte[] PackCommand(DeviceCommand command, ModbusConfig config)
    {
        _transactionId++;
        
        var request = new ModbusRequest
        {
            TransactionId = _transactionId,
            UnitId = config.SlaveId,
            SlaveId = config.SlaveId,
            FunctionCode = GetFunctionCode(command.CommandName),
            StartAddress = (ushort)command.Parameters.GetValueOrDefault("Address", 0),
            Quantity = (ushort)command.Parameters.GetValueOrDefault("Quantity", 1)
        };
        
        if (command.CommandName == "WriteSingleCoil")
        {
            request.Data = new byte[] { (bool)command.Parameters["Value"] ? (byte)0xFF : (byte)0x00, 0x00 };
        }
        else if (command.CommandName == "WriteSingleRegister")
        {
            var value = (ushort)command.Parameters["Value"];
            request.Data = new byte[] { (byte)(value >> 8), (byte)(value & 0xFF) };
        }
        
        return BuildTcpFrame(request);
    }

    private ModbusFunctionCode GetFunctionCode(string commandName)
    {
        return commandName switch
        {
            "ReadCoils" => ModbusFunctionCode.ReadCoils,
            "ReadDiscreteInputs" => ModbusFunctionCode.ReadDiscreteInputs,
            "ReadHoldingRegisters" => ModbusFunctionCode.ReadHoldingRegisters,
            "ReadInputRegisters" => ModbusFunctionCode.ReadInputRegisters,
            "WriteSingleCoil" => ModbusFunctionCode.WriteSingleCoil,
            "WriteSingleRegister" => ModbusFunctionCode.WriteSingleRegister,
            "WriteMultipleCoils" => ModbusFunctionCode.WriteMultipleCoils,
            "WriteMultipleRegisters" => ModbusFunctionCode.WriteMultipleRegisters,
            _ => throw new NotSupportedException($"不支持的命令: {commandName}")
        };
    }

    private byte[] BuildTcpFrame(ModbusRequest request)
    {
        var frame = new List<byte>();
        
        frame.Add((byte)(request.TransactionId >> 8));
        frame.Add((byte)(request.TransactionId & 0xFF));
        
        frame.Add(0x00);
        frame.Add(0x00);
        
        var pduLength = (byte)(request.Data != null ? request.Data.Length + 6 : 6);
        frame.Add(0x00);
        frame.Add(pduLength);
        
        frame.Add(request.UnitId);
        frame.Add((byte)request.FunctionCode);
        
        frame.Add((byte)(request.StartAddress >> 8));
        frame.Add((byte)(request.StartAddress & 0xFF));
        
        if (request.Data != null && request.Data.Length > 0)
        {
            frame.AddRange(request.Data);
        }
        else
        {
            frame.Add((byte)(request.Quantity >> 8));
            frame.Add((byte)(request.Quantity & 0xFF));
        }
        
        return frame.ToArray();
    }
}
