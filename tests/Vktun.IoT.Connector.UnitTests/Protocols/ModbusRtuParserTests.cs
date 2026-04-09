using System.Text.Json;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Protocol.Parsers;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.Protocols;

public class ModbusRtuParserTests
{
    private readonly ModbusRtuParser _parser;
    private readonly ProtocolConfig _config;

    public ModbusRtuParserTests()
    {
        _parser = new ModbusRtuParser(new MockLogger());
        _config = CreateDefaultConfig();
    }

    [Fact]
    public void Parse_ValidModbusResponse_ReturnsCorrectDataPoints()
    {
        var responseData = new byte[] { 0x01, 0x03, 0x04, 0x00, 0x64, 0x00, 0xC8 };
        var fullFrame = AppendCrc(responseData);

        var result = _parser.Parse(fullFrame, _config);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(2, result[0].DataItems.Count);
    }

    [Fact]
    public void Parse_InvalidCrc_CurrentBehaviorStillParses()
    {
        var invalidFrame = new byte[] { 0x01, 0x03, 0x04, 0x00, 0x64, 0x00, 0xC8, 0xFF, 0xFF };

        var result = _parser.Parse(invalidFrame, _config);

        Assert.Single(result);
    }

    [Fact]
    public void Parse_TooShortFrame_ReturnsEmptyList()
    {
        var shortFrame = new byte[] { 0x01, 0x03 };

        var result = _parser.Parse(shortFrame, _config);

        Assert.Empty(result);
    }

    [Fact]
    public void Pack_ReadHoldingRegistersCommand_GeneratesCorrectFrame()
    {
        var command = new DeviceCommand
        {
            DeviceId = "test_device",
            CommandName = "ReadHoldingRegisters",
            Parameters = new Dictionary<string, object>
            {
                { "Address", (ushort)0 },
                { "Quantity", (ushort)10 }
            }
        };

        var frame = _parser.Pack(command, _config);

        Assert.NotNull(frame);
        Assert.True(frame.Length >= 6);
        Assert.Equal(0x01, frame[0]);
        Assert.Equal(0x03, frame[1]);
    }

    [Fact]
    public void Validate_ValidFrame_ReturnsTrue()
    {
        var responseData = new byte[] { 0x01, 0x03, 0x04, 0x00, 0x64, 0x00, 0xC8 };
        var fullFrame = AppendCrc(responseData);

        var isValid = _parser.Validate(fullFrame, _config);

        Assert.True(isValid);
    }

    [Fact]
    public void Validate_InvalidFrame_ReturnsFalse()
    {
        var invalidFrame = new byte[] { 0x01, 0x03, 0x04, 0x00, 0x64, 0xFF, 0xFF };

        var isValid = _parser.Validate(invalidFrame, _config);

        Assert.False(isValid);
    }

    private static ProtocolConfig CreateDefaultConfig()
    {
        var modbusConfig = new ModbusConfig
        {
            ProtocolId = "modbus_test",
            ProtocolName = "Modbus RTU Test",
            ModbusType = ModbusType.Rtu,
            SlaveId = 1,
            ByteOrder = ByteOrder.BigEndian,
            Points = new List<ModbusPointConfig>
            {
                new()
                {
                    PointName = "temperature",
                    RegisterType = ModbusRegisterType.HoldingRegister,
                    Address = 0,
                    Quantity = 1,
                    DataType = DataType.UInt16,
                    Unit = "degC"
                },
                new()
                {
                    PointName = "humidity",
                    RegisterType = ModbusRegisterType.HoldingRegister,
                    Address = 1,
                    Quantity = 1,
                    DataType = DataType.UInt16,
                    Unit = "%RH"
                }
            }
        };

        var config = new ProtocolConfig
        {
            ProtocolId = "modbus_test",
            ProtocolType = ProtocolType.ModbusRtu,
            ParseRules = new Dictionary<string, string>()
        };

        var modbusJson = JsonSerializer.Serialize(modbusConfig);
        config.DefinitionJson = modbusJson;
        config.ParseRules["ModbusConfig"] = modbusJson;
        return config;
    }

    private static byte[] AppendCrc(byte[] data)
    {
        var crc = CalculateCrc16Modbus(data);
        var result = new byte[data.Length + 2];
        Array.Copy(data, result, data.Length);
        result[data.Length] = (byte)(crc & 0xFF);
        result[data.Length + 1] = (byte)((crc >> 8) & 0xFF);
        return result;
    }

    private static ushort CalculateCrc16Modbus(byte[] data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                if ((crc & 0x0001) != 0)
                {
                    crc >>= 1;
                    crc ^= 0xA001;
                }
                else
                {
                    crc >>= 1;
                }
            }
        }

        return crc;
    }

    private sealed class MockLogger : ILogger
    {
        public void Debug(string message)
        {
        }

        public void Info(string message)
        {
        }

        public void Warning(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }

        public void Fatal(string message, Exception? exception = null)
        {
        }

        public void Log(LogLevel level, string message, Exception? exception = null)
        {
        }
    }
}
