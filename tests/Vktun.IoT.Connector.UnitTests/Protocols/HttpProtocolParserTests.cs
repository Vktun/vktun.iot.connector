using System.Text;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Protocol.Factories;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.Protocols;

public class HttpProtocolParserTests
{
    [Fact]
    public void ProtocolParserFactory_ShouldRegisterHttpParser()
    {
        var factory = new ProtocolParserFactory(new TestLogger());

        var parser = factory.GetParser(ProtocolType.Http);

        Assert.NotNull(parser);
        Assert.Equal(ProtocolType.Http, parser.Type);
    }

    [Fact]
    public void HttpProtocolParser_ShouldPackAndParseRawPayload()
    {
        var parser = new ProtocolParserFactory(new TestLogger()).GetParser(ProtocolType.Http)!;
        var payload = Encoding.UTF8.GetBytes("{\"value\":42}");
        var command = new DeviceCommand
        {
            Data = payload
        };
        var config = new ProtocolConfig
        {
            ProtocolId = "Http",
            ProtocolName = "HTTP",
            ProtocolType = ProtocolType.Http,
            ChannelId = "http-test"
        };

        var packed = parser.Pack(command, config);
        var parsed = parser.Parse(packed, config);

        Assert.Equal(payload, packed);
        Assert.Single(parsed);
        Assert.True(parsed[0].IsValid);
        Assert.Equal(payload, parsed[0].RawData);
        Assert.Equal("http-test", parsed[0].ChannelId);
    }

    private sealed class TestLogger : ILogger
    {
        public void Log(LogLevel level, string message, Exception? exception = null)
        {
        }

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
    }
}
