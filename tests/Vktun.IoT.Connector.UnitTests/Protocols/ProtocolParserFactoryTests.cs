using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Protocol.Factories;
using Xunit;

namespace Vktun.IoT.Connector.UnitTests.Protocols;

public class ProtocolParserFactoryTests
{
    [Fact]
    public void GetParser_ForCustomType_ShouldPreferStableCustomParser()
    {
        var factory = new ProtocolParserFactory(new TestLogger());

        var parser = factory.GetParser(ProtocolType.Custom);

        Assert.NotNull(parser);
        Assert.Equal("CustomProtocolParser", parser.Name);
        Assert.Equal(ParserStatus.Stable, parser.Status);
    }

    [Theory]
    [InlineData("OPC UA", ParserStatus.Experimental)]
    [InlineData("BACnet", ParserStatus.Experimental)]
    [InlineData("CANopen", ParserStatus.Experimental)]
    public void GetParser_ByExplicitAlias_ShouldResolveExperimentalParser(string alias, ParserStatus expectedStatus)
    {
        var factory = new ProtocolParserFactory(new TestLogger());

        var parser = factory.GetParser(alias);

        Assert.NotNull(parser);
        Assert.Equal(alias, parser.Name);
        Assert.Equal(expectedStatus, parser.Status);
    }

    [Fact]
    public void GetAllParserDescriptors_ShouldIncludeStableAndExperimentalEntriesWithoutLosingCustomParser()
    {
        var factory = new ProtocolParserFactory(new TestLogger());

        var descriptors = factory.GetAllParserDescriptors().ToList();

        Assert.Contains(descriptors, descriptor => descriptor.Name == "CustomProtocolParser" && descriptor.Status == ParserStatus.Stable);
        Assert.Contains(descriptors, descriptor => descriptor.Name == "OPC UA" && descriptor.Status == ParserStatus.Experimental);
        Assert.Contains(descriptors, descriptor => descriptor.Name == "BACnet" && descriptor.Status == ParserStatus.Experimental);
        Assert.Contains(descriptors, descriptor => descriptor.Name == "CANopen" && descriptor.Status == ParserStatus.Experimental);
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
