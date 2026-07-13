namespace XsvHcdtHelper.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using XsvHcdtHelper;

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
}
