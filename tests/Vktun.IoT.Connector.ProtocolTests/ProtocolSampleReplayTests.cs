using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vktun.IoT.Connector.Configuration.Logging;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;
using Vktun.IoT.Connector.Protocol.Factories;
using Xunit;

namespace Vktun.IoT.Connector.ProtocolTests;

public class ProtocolSampleReplayTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IEnumerable<object[]> SampleReplayCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SampleData", "protocol-message-library.json");
        var library = JsonSerializer.Deserialize<ProtocolMessageLibrary>(File.ReadAllText(path), JsonOptions);
        Assert.NotNull(library);
        Assert.NotEmpty(library.Cases);

        foreach (var sample in library.Cases)
        {
            yield return new object[] { sample };
        }
    }

    [Theory]
    [MemberData(nameof(SampleReplayCases))]
    public void ReplaySample_RequestAndResponse_ShouldRemainCompatible(ProtocolReplaySample sample)
    {
        var logger = new SerilogLogger();
        var factory = new ProtocolParserFactory(logger);
        var config = CreateProtocolConfig(sample);
        var parser = factory.GetParser(config.ProtocolType);
        Assert.NotNull(parser);

        var command = CreateCommand(sample);
        var requestFrame = parser.Pack(command, config);
        Assert.Equal(NormalizeHex(sample.RequestHex), ToHex(requestFrame));

        var responseFrame = FromHex(sample.ResponseHex);
        Assert.True(parser.Validate(responseFrame, config), $"Sample '{sample.Id}' response frame must pass validation.");

        var parsed = parser.Parse(responseFrame, config);
        var deviceData = Assert.Single(parsed);
        Assert.True(deviceData.IsValid);
        Assert.Equal(sample.ExpectedDeviceId, deviceData.DeviceId);
        Assert.Equal(sample.ExpectedPointCount, deviceData.DataItems.Count);

        foreach (var expectedPoint in sample.ExpectedPoints)
        {
            var actualPoint = Assert.Single(deviceData.DataItems, p => p.PointName == expectedPoint.PointName);
            Assert.True(actualPoint.IsValid);
            Assert.Equal(expectedPoint.Value, Convert.ToDouble(actualPoint.Value, CultureInfo.InvariantCulture), precision: 3);
        }
    }

    private static ProtocolConfig CreateProtocolConfig(ProtocolReplaySample sample)
    {
        var protocolType = Enum.Parse<ProtocolType>(sample.ProtocolType, ignoreCase: true);
        var config = new ProtocolConfig
        {
            ProtocolId = sample.ProtocolId,
            ProtocolName = sample.ProtocolName,
            ProtocolType = protocolType,
            ChannelId = $"{sample.ProtocolId}_Channel"
        };

        if (protocolType is ProtocolType.ModbusTcp or ProtocolType.ModbusRtu)
        {
            var modbusConfig = sample.Definition.Deserialize<ModbusConfig>(JsonOptions);
            Assert.NotNull(modbusConfig);
            config.SetDefinition(modbusConfig);
        }
        else
        {
            config.DefinitionJson = sample.Definition.GetRawText();
        }

        return config;
    }

    private static DeviceCommand CreateCommand(ProtocolReplaySample sample)
    {
        var command = new DeviceCommand
        {
            DeviceId = sample.ProtocolId,
            CommandName = sample.CommandName
        };

        foreach (var parameter in sample.CommandParameters)
        {
            command.Parameters[parameter.Key] = ConvertJsonValue(parameter.Value);
        }

        return command;
    }

    private static object ConvertJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when value.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => value.GetString() ?? string.Empty,
            _ => value.GetRawText()
        };
    }

    private static byte[] FromHex(string hex)
    {
        var normalized = NormalizeHex(hex);
        var bytes = new byte[normalized.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(normalized.Substring(i * 2, 2), 16);
        }

        return bytes;
    }

    private static string ToHex(byte[] bytes)
    {
        return Convert.ToHexString(bytes);
    }

    private static string NormalizeHex(string hex)
    {
        return string.Concat(hex.Where(Uri.IsHexDigit)).ToUpperInvariant();
    }

    public sealed class ProtocolMessageLibrary
    {
        public int Version { get; set; }
        public List<ProtocolReplaySample> Cases { get; set; } = new();
    }

    public sealed class ProtocolReplaySample
    {
        public string Id { get; set; } = string.Empty;
        public string ProtocolId { get; set; } = string.Empty;
        public string ProtocolName { get; set; } = string.Empty;
        public string ProtocolType { get; set; } = string.Empty;
        public string CommandName { get; set; } = string.Empty;
        public Dictionary<string, JsonElement> CommandParameters { get; set; } = new();
        public string RequestHex { get; set; } = string.Empty;
        public string ResponseHex { get; set; } = string.Empty;
        public string ExpectedDeviceId { get; set; } = string.Empty;
        public int ExpectedPointCount { get; set; }
        public List<ExpectedReplayPoint> ExpectedPoints { get; set; } = new();
        public JsonElement Definition { get; set; }
    }

    public sealed class ExpectedReplayPoint
    {
        public string PointName { get; set; } = string.Empty;
        public double Value { get; set; }
    }
}
