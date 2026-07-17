namespace XsvHcdtHelper.Tests;

using System.Text;
using FluentAssertions;
using Xunit;
using XsvHcdtHelper;

public class DelimiterDetectionTests
{
    [Fact]
    public async Task NormaliseAsync_GivenAutoDelimiterAndPipeShapedFile_DetectsPipeAndParsesCorrectly()
    {
        const string input = """
            H|AUTO_PIPE.csv|14072026 14:30:00
            C|RECORD_TYPE|ID|NAME
            D|1|Alice
            T|AUTO_PIPE.csv|14072026 14:30:00|1
            """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();

        var report = await new XsvHcdtNormaliser().NormaliseAsync(
            inputStream, outputStream, o => o.InputDelimiter = FieldDelimiter.Auto);

        report.Columns.Should().BeEquivalentTo(new[] { "RECORD_TYPE", "ID", "NAME" }, opts => opts.WithStrictOrdering());
        report.ActualDataRecords.Should().Be(1);

        outputStream.Position = 0;
        var csv = await new StreamReader(outputStream).ReadToEndAsync();
        csv.Should().Contain("D,1,Alice");
    }

    [Fact]
    public async Task NormaliseAsync_GivenAutoDelimiterAndCommaShapedFile_DetectsCommaAndParsesCorrectly()
    {
        const string input = "H,AUTO_COMMA.csv,14072026 14:30:00\n" +
                              "C,RECORD_TYPE,ID,NAME\n" +
                              "D,1,Alice\n" +
                              "T,AUTO_COMMA.csv,14072026 14:30:00,1\n";

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();

        var report = await new XsvHcdtNormaliser().NormaliseAsync(
            inputStream, outputStream, o => o.InputDelimiter = FieldDelimiter.Auto);

        report.Columns.Should().BeEquivalentTo(new[] { "RECORD_TYPE", "ID", "NAME" }, opts => opts.WithStrictOrdering());
        report.ActualDataRecords.Should().Be(1);
    }

    [Fact]
    public async Task NormaliseAsync_GivenExplicitDelimiter_OverridesWhatAutoDetectionWouldInfer()
    {
        // Shaped as a comma file - Auto would successfully detect Comma and parse cleanly.
        const string input = "H,OVERRIDE.csv,14072026 14:30:00\n" +
                              "C,ID,NAME\n" +
                              "D,1,Alice\n" +
                              "T,OVERRIDE.csv,14072026 14:30:00,1\n";

        using (var autoInput = new MemoryStream(Encoding.UTF8.GetBytes(input)))
        using (var autoOutput = new MemoryStream())
        {
            var autoReport = await new XsvHcdtNormaliser().NormaliseAsync(
                autoInput, autoOutput, o => o.InputDelimiter = FieldDelimiter.Auto);
            autoReport.ActualDataRecords.Should().Be(1);
        }

        // Act - force Pipe explicitly on the same comma-shaped content.
        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();

        Func<Task> act = async () => await new XsvHcdtNormaliser().NormaliseAsync(
            inputStream, outputStream, o => o.InputDelimiter = FieldDelimiter.Pipe);

        // Assert - the explicit Pipe setting is honoured rather than falling back
        // to what auto-detection would have inferred (Comma), so parsing fails
        // because no record actually contains a pipe character.
        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*single-character H, C, D, or T tag*");
    }
}