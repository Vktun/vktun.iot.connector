using Vktun.IoT.Connector.Configuration.Logging;
using Vktun.IoT.Connector.Configuration.Providers;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Protocol.Parsers;
using Xunit;

namespace Vktun.IoT.Connector.ProtocolTests;

public class ModbusTcpParserTests
{
    private readonly ILogger _logger;

    public ModbusTcpParserTests()
    {
        _logger = new SerilogLogger();
    }

    [Fact]
    public void Parse_ReadHoldingRegisters_ShouldReturnCorrectValues()
    {
        var parser = new ModbusTcpParser(_logger);
        var response = BuildReadHoldingRegistersResponse(1, new ushort[] { 0x1234, 0x5678 });
        var config = CreateModbusTcpConfig();

        var result = parser.Parse(response, config);

        Assert.Single(result);
        Assert.True(result[0].IsValid);
    }

    [Fact]
    public void Pack_ReadHoldingRegisters_ShouldProduceValidFrame()
    {
        var parser = new ModbusTcpParser(_logger);
        var command = new DeviceCommand
        {
            DeviceId = "test",
            CommandName = "ReadHoldingRegisters",
            Parameters = { ["SlaveId"] = (ushort)1, ["StartAddress"] = (ushort)0, ["Quantity"] = (ushort)10 }
        };
        var config = CreateModbusTcpConfig();

        var frame = parser.Pack(command, config);

        Assert.NotEmpty(frame);
        Assert.True(frame.Length >= 12);
    }

    [Fact]
    public void Validate_EmptyFrame_ShouldReturnFalse()
    {
        var parser = new ModbusTcpParser(_logger);
        var config = CreateModbusTcpConfig();

        Assert.False(parser.Validate(Array.Empty<byte>(), config));
    }

    [Fact]
    public void Validate_ShortFrame_ShouldReturnFalse()
    {
        var parser = new ModbusTcpParser(_logger);
        var config = CreateModbusTcpConfig();

        Assert.False(parser.Validate(new byte[] { 0x00 }, config));
    }

    private static byte[] BuildReadHoldingRegistersResponse(byte slaveId, ushort[] values)
    {
        var byteCount = (byte)(values.Length * 2);
        var data = new List<byte> { slaveId, 0x03, byteCount };
        foreach (var v in values)
        {
            data.Add((byte)(v >> 8));
            data.Add((byte)(v & 0xFF));
        }

        var mbapHeader = new List<byte>();
        mbapHeader.AddRange(BitConverter.GetBytes((ushort)0).Reverse());
        mbapHeader.AddRange(BitConverter.GetBytes((ushort)0x0001).Reverse());
        mbapHeader.Add((byte)0);
        mbapHeader.Add((byte)1);
        mbapHeader.AddRange(BitConverter.GetBytes((ushort)(data.Count + 1)).Reverse());

        return mbapHeader.Concat(data).ToArray();
    }

    private static ProtocolConfig CreateModbusTcpConfig()
    {
        var config = new ProtocolConfig
        {
            ProtocolId = "ModbusTcp",
            ProtocolName = "Modbus TCP",
            ProtocolType = ProtocolType.ModbusTcp,
            Points = new List<PointConfig>
            {
                new() { PointName = "Temperature", Offset = 0, DataType = DataType.UInt16 }
            }
        };
        config.SetDefinition(new ModbusConfig
        {
            ProtocolId = "ModbusTcp",
            SlaveId = 1,
            ModbusType = ModbusType.Tcp,
            Points = new List<ModbusPointConfig>
            {
                new() { PointName = "Temperature", RegisterType = ModbusRegisterType.HoldingRegister, Address = 0, Quantity = 1, DataType = DataType.UInt16 }
            }
        });
        return config;
    }
}

public class ModbusRtuParserTests
{
    private readonly ILogger _logger;

    public ModbusRtuParserTests()
    {
        _logger = new SerilogLogger();
    }

    [Fact]
    public void Validate_EmptyFrame_ShouldReturnFalse()
    {
        var parser = new ModbusRtuParser(_logger);
        var config = CreateModbusRtuConfig();

        Assert.False(parser.Validate(Array.Empty<byte>(), config));
    }

    [Fact]
    public void Validate_CrcError_ShouldReturnFalse()
    {
        var parser = new ModbusRtuParser(_logger);
        var config = CreateModbusRtuConfig();

        var frame = new byte[] { 0x01, 0x03, 0x02, 0x00, 0x01, 0xFF, 0xFF };
        Assert.False(parser.Validate(frame, config));
    }

    [Fact]
    public void Pack_ReadHoldingRegisters_ShouldIncludeCrc()
    {
        var parser = new ModbusRtuParser(_logger);
        var command = new DeviceCommand
        {
            DeviceId = "test",
            CommandName = "ReadHoldingRegisters",
            Parameters = { ["SlaveId"] = (ushort)1, ["StartAddress"] = (ushort)0, ["Quantity"] = (ushort)10 }
        };
        var config = CreateModbusRtuConfig();

        var frame = parser.Pack(command, config);

        Assert.NotEmpty(frame);
        Assert.True(frame.Length >= 8);
    }

    private static ProtocolConfig CreateModbusRtuConfig()
    {
        var config = new ProtocolConfig
        {
            ProtocolId = "ModbusRtu",
            ProtocolName = "Modbus RTU",
            ProtocolType = ProtocolType.ModbusRtu,
            Points = new List<PointConfig>
            {
                new() { PointName = "Value", Offset = 0, DataType = DataType.UInt16 }
            }
        };
        config.SetDefinition(new ModbusConfig
        {
            ProtocolId = "ModbusRtu",
            SlaveId = 1,
            ModbusType = ModbusType.Rtu,
            Points = new List<ModbusPointConfig>
            {
                new() { PointName = "Value", RegisterType = ModbusRegisterType.HoldingRegister, Address = 0, Quantity = 1, DataType = DataType.UInt16 }
            }
        });
        return config;
    }
}

public class S7ProtocolParserTests
{
    private readonly ILogger _logger;

    public S7ProtocolParserTests()
    {
        _logger = new SerilogLogger();
    }

    [Fact]
    public void Validate_EmptyFrame_ShouldReturnFalse()
    {
        var parser = new S7ProtocolParser(_logger);
        var config = CreateS7Config();

        Assert.False(parser.Validate(Array.Empty<byte>(), config));
    }

    [Fact]
    public void Validate_ShortFrame_ShouldReturnFalse()
    {
        var parser = new S7ProtocolParser(_logger);
        var config = CreateS7Config();

        Assert.False(parser.Validate(new byte[4], config));
    }

    private static ProtocolConfig CreateS7Config()
    {
        return new ProtocolConfig
        {
            ProtocolId = "S7",
            ProtocolName = "S7",
            ProtocolType = ProtocolType.S7,
            Points = new List<PointConfig>
            {
                new() { PointName = "DB1.DBW0", Offset = 0, DataType = DataType.UInt16 }
            }
        };
    }
}

public class IEC104ProtocolParserTests
{
    private readonly ILogger _logger;

    public IEC104ProtocolParserTests()
    {
        _logger = new SerilogLogger();
    }

    [Fact]
    public void Validate_EmptyFrame_ShouldReturnFalse()
    {
        var parser = new IEC104ProtocolParser(_logger);
        var config = CreateIEC104Config();

        Assert.False(parser.Validate(Array.Empty<byte>(), config));
    }

    [Fact]
    public void Validate_StartFrame_ShouldReturnTrue()
    {
        var parser = new IEC104ProtocolParser(_logger);
        var config = CreateIEC104Config();

        var startFrame = new byte[] { 0x68, 0x04, 0x07, 0x00, 0x00, 0x00 };
        Assert.True(parser.Validate(startFrame, config));
    }

    private static ProtocolConfig CreateIEC104Config()
    {
        return new ProtocolConfig
        {
            ProtocolId = "IEC104",
            ProtocolName = "IEC 60870-5-104",
            ProtocolType = ProtocolType.IEC104,
            Points = new List<PointConfig>
            {
                new() { PointName = "Point1", Offset = 0, DataType = DataType.Float }
            }
        };
    }
}

public class CustomProtocolParserTests
{
    private readonly ILogger _logger;

    public CustomProtocolParserTests()
    {
        _logger = new SerilogLogger();
    }

    [Fact]
    public void Parse_WithFrameHeaderAndTail_ShouldExtractData()
    {
        var parser = new CustomProtocolParser(_logger);
        var config = CreateCustomConfig();
        var frame = new byte[] { 0xAA, 0x55, 0x00, 0x01, 0x0D };
        var result = parser.Parse(frame, config);

        Assert.Single(result);
        Assert.True(result[0].IsValid);
    }

    [Fact]
    public void Validate_WrongHeader_ShouldReturnFalse()
    {
        var parser = new CustomProtocolParser(_logger);
        var config = CreateCustomConfig();
        var frame = new byte[] { 0xBB, 0x55, 0x00, 0x01, 0x0D };

        Assert.False(parser.Validate(frame, config));
    }

    [Fact]
    public void Validate_ShortFrame_ShouldReturnFalse()
    {
        var parser = new CustomProtocolParser(_logger);
        var config = CreateCustomConfig();

        Assert.False(parser.Validate(new byte[] { 0xAA }, config));
    }

    [Fact]
    public void Pack_ResponseFrame_ShouldIncludeHeaderAndTail()
    {
        var parser = new CustomProtocolParser(_logger);
        var config = CreateCustomConfig();
        var data = new DeviceData
        {
            DeviceId = "01",
            DataItems = new List<DataPoint>
            {
                new() { PointName = "Value", Value = 13.0, DataType = DataType.UInt8 }
            }
        };

        var frame = parser.Pack(data, config);

        Assert.NotEmpty(frame);
        Assert.Equal(0xAA, frame[0]);
        Assert.Equal(0x55, frame[1]);
    }

    private static ProtocolConfig CreateCustomConfig()
    {
        var customConfig = new CustomProtocolConfig
        {
            ProtocolId = "Custom1",
            ProtocolName = "Test Custom",
            FrameType = FrameType.FixedLength,
            ByteOrder = ByteOrder.BigEndian,
            FrameHeader = new FrameHeaderConfig { Value = new byte[] { 0xAA, 0x55 }, Length = 2 },
            FrameTail = new FrameTailConfig { Value = new byte[] { 0x0D }, Length = 1 },
            Points = new List<PointConfig>
            {
                new() { PointName = "Value", Offset = 0, Length = 1, DataType = DataType.UInt8 }
            }
        };

        var config = new ProtocolConfig
        {
            ProtocolId = "Custom1",
            ProtocolName = "Test Custom",
            ProtocolType = ProtocolType.Custom
        };
        config.SetDefinition(customConfig);
        return config;
    }
}
