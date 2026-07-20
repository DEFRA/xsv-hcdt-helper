namespace XsvHcdtHelper.Tests;

using FluentAssertions;
using Xunit;
using XsvHcdtHelper;
using System.Text;

public class StaticFacadeTests
{
    private const string ValidPipeInput = """
        H|CTSM_UKV.csv|22022026 07:46:03
        C|RECORD_TYPE|COL1
        D|VAL1
        T|CTSM_UKV.csv|22022026 07:46:03|1
        """;

    [Fact]
    public async Task XsvHcdt_NormaliseAsync_GivenValidStream_DelegatesCorrectly()
    {
        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(ValidPipeInput));
        using var outputStream = new MemoryStream();

        var report = await XsvHcdt.NormaliseAsync(inputStream, outputStream, o => o.InputDelimiter = FieldDelimiter.Pipe);

        report.ActualDataRecords.Should().Be(1);
        outputStream.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task XsvHcdt_NormaliseFileAsync_GivenPhysicalFiles_ProcessesAndCleansUp()
    {
        var inputPath = Path.GetTempFileName();
        var outputPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(inputPath, ValidPipeInput);

        try
        {
            var report = await XsvHcdt.NormaliseFileAsync(inputPath, outputPath, o => o.InputDelimiter = FieldDelimiter.Pipe);

            report.ActualDataRecords.Should().Be(1);
            var outputContent = await File.ReadAllTextAsync(outputPath);
            outputContent.Should().Contain("VAL1");
        }
        finally
        {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }
}
