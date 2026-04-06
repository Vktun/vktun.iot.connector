using Vktun.IoT.Connector.Core.Enums;
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
        var logger = new MockLogger();
        _parser = new ModbusRtuParser(logger);
        _config = CreateDefaultConfig();
    }

    [Fact]
    public void Parse_ValidModbusResponse_ReturnsCorrectDataPoints()
    {
        // Arrange: 构造一个有效的Modbus RTU响应帧
        // 从站地址: 0x01, 功能码: 0x03 (读保持寄存器), 字节数: 0x04, 数据: 0x00 0x64 0x00 0xC8, CRC: 0xXX 0xXX
        var responseData = new byte[] { 0x01, 0x03, 0x04, 0x00, 0x64, 0x00, 0xC8 };
        var fullFrame = AppendCrc(responseData); // 添加CRC校验

        // Act
        var result = _parser.Parse(fullFrame, _config);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count); // 应该解析出2个数据点（根据配置）
    }

    [Fact]
    public void Parse_InvalidCrc_ReturnsEmptyList()
    {
        // Arrange: 构造一个CRC错误的帧
        var responseData = new byte[] { 0x01, 0x03, 0x04, 0x00, 0x64, 0x00, 0xC8, 0xFF, 0xFF }; // 错误的CRC

        // Act
        var result = _parser.Parse(responseData, _config);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_TooShortFrame_ReturnsEmptyList()
    {
        // Arrange: 构造一个过短的帧
        var shortFrame = new byte[] { 0x01, 0x03 };

        // Act
        var result = _parser.Parse(shortFrame, _config);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Pack_ReadHoldingRegistersCommand_GeneratesCorrectFrame()
    {
        // Arrange
        var command = new DeviceCommand
        {
            DeviceId = "test_device",
            CommandName = "CollectData",
            Parameters = new Dictionary<string, object>
            {
                { "StartAddress", 0 },
                { "Quantity", 10 }
            }
        };

        // Act
        var frame = _parser.Pack(command, _config);

        // Assert
        Assert.NotNull(frame);
        Assert.True(frame.Length >= 6); // Modbus RTU请求帧至少6字节
        Assert.Equal(0x01, frame[0]); // 从站地址
        Assert.Equal(0x03, frame[1]); // 功能码
    }

    [Fact]
    public void Validate_ValidFrame_ReturnsTrue()
    {
        // Arrange
        var responseData = new byte[] { 0x01, 0x03, 0x04, 0x00, 0x64, 0x00, 0xC8 };
        var fullFrame = AppendCrc(responseData);

        // Act
        var isValid = _parser.Validate(fullFrame, _config);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void Validate_InvalidFrame_ReturnsFalse()
    {
        // Arrange
        var invalidFrame = new byte[] { 0x01, 0x03, 0x04, 0x00, 0x64, 0xFF, 0xFF };

        // Act
        var isValid = _parser.Validate(invalidFrame, _config);

        // Assert
        Assert.False(isValid);
    }

    private static ProtocolConfig CreateDefaultConfig()
    {
        return new ProtocolConfig
        {
            ProtocolId = "modbus_test",
            ProtocolType = ProtocolType.ModbusRtu,
            AdditionalSettings = new Dictionary<string, object?>
            {
                ["SlaveId"] = 1,
                ["ByteOrder"] = "BigEndian"
            },
            Points = new List<PointConfig>
            {
                new PointConfig
                {
                    PointName = "temperature",
                    Offset = 0,
                    DataType = DataType.UInt16,
                    Unit = "°C"
                },
                new PointConfig
                {
                    PointName = "humidity",
                    Offset = 1,
                    DataType = DataType.UInt16,
                    Unit = "%RH"
                }
            }
        };
    }

    private static byte[] AppendCrc(byte[] data)
    {
        // 简单的CRC16 Modbus计算（实际应使用CrcCalculator）
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
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
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

    // 模拟日志器（用于测试）
    private class MockLogger : global::Vktun.IoT.Connector.Core.Interfaces.ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public void Fatal(string message, Exception? exception = null) { }
        public void Log(global::Vktun.IoT.Connector.Core.Enums.LogLevel level, string message, Exception? exception = null) { }
    }
}
