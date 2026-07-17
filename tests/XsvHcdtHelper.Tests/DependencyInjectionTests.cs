namespace XsvHcdtHelper.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using XsvHcdtHelper;
using Microsoft.Extensions.Configuration;
using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;

public class DependencyInjectionTests
{
    [Fact]
    public void AddXsvHcdtHelper_ZeroConfig_RegistersNormaliserWithDefaults()
    {
        var services = new ServiceCollection();

        services.AddXsvHcdtHelper();
        var provider = services.BuildServiceProvider();

        var normaliser = provider.GetService<IXsvHcdtNormaliser>();
        normaliser.Should().NotBeNull();
        normaliser.Should().BeOfType<XsvHcdtNormaliser>();

        var reader = provider.GetService<IXsvHcdtReader>();
        reader.Should().NotBeNull();
        reader.Should().BeOfType<XsvHcdtReader>();

        var options = provider.GetRequiredService<IOptions<XsvHcdtOptions>>().Value;
        options.InputDelimiter.Should().Be(FieldDelimiter.Auto);
        options.OutputFormat.Should().Be(OutputFormat.Csv);
        options.ValidateTrailerCount.Should().BeTrue();
    }

    [Fact]
    public void AddXsvHcdtHelper_WithAction_ConfiguresOptions()
    {
        var services = new ServiceCollection();

        services.AddXsvHcdtHelper(opts =>
        {
            opts.OutputFormat = OutputFormat.Parquet;
            opts.StrictFieldCount = true;
        });

        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<XsvHcdtOptions>>().Value;
        options.OutputFormat.Should().Be(OutputFormat.Parquet);
        options.StrictFieldCount.Should().BeTrue();

        options.InputDelimiter.Should().Be(FieldDelimiter.Auto);
    }

    [Fact]
    public void AddXsvHcdtHelper_WithIConfiguration_BindsOptionsCorrectly()
    {
        // Arrange
        // Simulate reading from an appsettings.json section
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"OutputFormat", "Parquet"},
            {"StrictFieldCount", "true"},
            {"RowGroupSize", "1000"}
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddXsvHcdtHelper(configuration);
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<XsvHcdtOptions>>().Value;

        // Values overridden by config
        options.OutputFormat.Should().Be(OutputFormat.Parquet);
        options.StrictFieldCount.Should().BeTrue();
        options.RowGroupSize.Should().Be(1000);

        // Values NOT in config should keep their sensible defaults
        options.InputDelimiter.Should().Be(FieldDelimiter.Auto);
        options.ValidateTrailerCount.Should().BeTrue();
    }

    [Fact]
    public async Task NormaliseAsync_PerCallConfigure_OverridesRegisteredOptionsForSingleOperation()
    {
        var services = new ServiceCollection();
        services.AddXsvHcdtHelper(o => o.OutputFormat = OutputFormat.Parquet);
        var provider = services.BuildServiceProvider();

        var normaliser = provider.GetRequiredService<IXsvHcdtNormaliser>();

        const string input = "H|F.csv|22022026 07:46:03\n" +
                             "C|RECORD_TYPE|ID\n" +
                             "D|1\n" +
                             "T|F.csv|22022026 07:46:03|1\n";

        using var inputStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(input));
        using var outputStream = new System.IO.MemoryStream();

        // Per-call override: CSV for this operation despite Parquet being registered.
        await normaliser.NormaliseAsync(inputStream, outputStream, o => o.OutputFormat = OutputFormat.Csv);

        var text = System.Text.Encoding.UTF8.GetString(outputStream.ToArray());
        text.Should().StartWith("RECORD_TYPE,ID", "the per-call delegate must win over the registered options");

        // The registered defaults are untouched for subsequent operations.
        provider.GetRequiredService<IOptions<XsvHcdtOptions>>().Value.OutputFormat.Should().Be(OutputFormat.Parquet);
    }
}
