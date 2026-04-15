using Vktun.IoT.Connector.Configuration.Logging;
using Vktun.IoT.Connector.Configuration.Providers;
using Vktun.IoT.Connector.Core.Enums;
using Vktun.IoT.Connector.Protocol.Factories;
using Xunit;

namespace Vktun.IoT.Connector.ProtocolTests;

public class ProtocolTemplateCompatibilityTests
{
    private static readonly string[] TemplateDirectories =
    {
        Path.Combine("src", "Vktun.IoT.Connector.Protocol", "Templates"),
        "templates",
        Path.Combine("demo", "Vktun.IoT.Connector.Demo", "Protocols")
    };

    public static IEnumerable<object[]> TemplateFiles()
    {
        var root = FindRepositoryRoot();
        foreach (var directory in TemplateDirectories.Select(d => Path.Combine(root, d)))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories).OrderBy(f => f))
            {
                yield return new object[] { file };
            }
        }
    }

    [Theory]
    [MemberData(nameof(TemplateFiles))]
    public async Task LoadTemplate_ShouldProduceValidProtocolConfig(string templateFile)
    {
        var logger = new SerilogLogger();
        var provider = new JsonConfigurationProvider(logger);
        var factory = new ProtocolParserFactory(logger);

        var config = await provider.LoadProtocolTemplateAsync(templateFile);
        Assert.NotNull(config);
        Assert.False(string.IsNullOrWhiteSpace(config.ProtocolId));
        Assert.False(string.IsNullOrWhiteSpace(config.ProtocolName));
        Assert.False(string.IsNullOrWhiteSpace(config.DefinitionJson));
        Assert.Equal(templateFile, config.TemplateSource);

        var report = provider.ValidateTemplate(config);
        Assert.True(report.IsValid, $"{templateFile} validation failed: {string.Join("; ", report.Errors)}");
        Assert.NotNull(factory.GetParser(config.ProtocolType));

        if (config.ProtocolType is ProtocolType.ModbusTcp or ProtocolType.ModbusRtu)
        {
            var definition = config.GetDefinition<Core.Models.ModbusConfig>();
            Assert.NotNull(definition);
            Assert.NotEmpty(definition.Points);
            Assert.True(config.ParseRules.ContainsKey("ModbusConfig"));
        }

        var version = await provider.GetTemplateVersionAsync(templateFile);
        Assert.NotNull(version);
        Assert.True(version.FileSize > 0);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Vktun.IoT.Connector.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root from test output directory.");
    }
}
