using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Core.Utils;
using Vktun.IoT.Connector.Protocol.Parsers;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.Protocols;

public class ModbusTypedParserTests
{
    [Fact]
    public void ModbusRtuParser_ParseUInt64_ShouldPreserveAllEightBytes()
    {
        var parser = new ModbusRtuParser(new TestLogger());
        var config = new ProtocolConfig
        {
            ProtocolId = "typed-rtu",
            ProtocolType = ProtocolType.ModbusRtu
        };
        config.SetDefinition(new ModbusConfig
        {
            ModbusType = ModbusType.Rtu,
            SlaveId = 1,
            ByteOrder = ByteOrder.BigEndian,
            WordOrder = WordOrder.HighWordFirst,
            Points =
            {
                new ModbusPointConfig
                {
                    PointName = "Counter",
                    RegisterType = ModbusRegisterType.HoldingRegister,
                    Address = 0,
                    DataType = DataType.UInt64
                }
            }
        });

        var payload = new byte[] { 0x01, 0x03, 0x08, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        var crc = CrcCalculator.Crc16Modbus(payload);
        var frame = payload.Concat(new[] { (byte)(crc & 0xFF), (byte)(crc >> 8) }).ToArray();

        var data = Assert.Single(parser.Parse(frame, config));
        var point = Assert.Single(data.DataItems);
        Assert.Equal(4294967297d, Assert.IsType<double>(point.Value));
    }

    private sealed class TestLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public void Fatal(string message, Exception? exception = null) { }
        public void Log(LogLevel level, string message, Exception? exception = null) { }
    }
}
