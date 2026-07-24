namespace XsvHcdtHelper.Tests;

using System.Text;
using FluentAssertions;
using Parquet;
using Xunit;
using XsvHcdtHelper;
using XsvHcdtHelper.Tests.Helpers;

public class XsvHcdtIntegrityTests
{
    // C has 6 columns. D must result in 6 fields (Tag + 5 values).
    private const string ValidHcdtWithKnownValue =
        "H|f.csv|d\n" +
        "C|RECORD_TYPE|RECORD_COUNT|ETF_ID|ETF_DESCRIPTION|ETF_STATUS|ETF_CURRENT_MODIFIED_DATE\n" +
        "D|3|Pre-Barimo|Desc||13-OCT-99\n" +
        "T|f.csv|d|1\n";

    // C has 6 columns. D here results in 4 fields (D, 3, ID, Date).
    private const string FieldCountMismatchHcdt =
        "H|f.csv|d\n" +
        "C|RECORD_TYPE|RECORD_COUNT|ETF_ID|ETF_STATUS|ETF_DESC|ETF_CURRENT_MODIFIED_DATE\n" +
        "D|3|Pre-Barimo|13-OCT-99\n" +
        "T|f.csv|d|1\n";

    [Fact]
    public async Task ReadAsync_DoesNotStripDTagFromFields()
    {
        // C: 3 columns. D: 3 fields (D, 1, Test).
        const string input = "H|file.csv|date\n" +
                             "C|RECORD_TYPE|RECORD_COUNT|VALUE\n" +
                             "D|1|Test\n" +
                             "T|file.csv|date|1\n";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        var reader = new XsvHcdtReader();
        var records = new List<XsvRecord>();

        await foreach (var record in reader.ReadAsync(stream, o => o.InputDelimiter = FieldDelimiter.Pipe))
        {
            records.Add(record);
        }

        var dataRecord = records.First(r => r.Tag == 'D');

        dataRecord.Fields.Should().HaveCount(3);
        dataRecord.Fields[0].Should().Be("D");
        dataRecord.Fields[2].Should().Be("Test");
    }

    [Fact]
    public async Task NormaliseAsync_OnNonSeekableStream_TrailerMismatchThrowsValidationException()
    {
        const string input = "H|file.csv|date\n" +
                             "C|COL\n" +
                             "D|Val\n" +
                             "T|file.csv|date|999\n";

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        await using var outputStream = new NonSeekableWriteStream(new MemoryStream());
        var normaliser = new XsvHcdtNormaliser();

        var act = async () => await normaliser.NormaliseAsync(inputStream, outputStream, o => o.InputDelimiter = FieldDelimiter.Pipe);

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*Declared record count (999) does not match actual count (1)*");
    }

    [Fact]
    public async Task NormaliseAsync_OnNonSeekableStream_FieldMismatchThrowsValidationException()
    {
        using var input = ToStream(FieldCountMismatchHcdt);
        await using var output = new NonSeekableWriteStream(new MemoryStream());

        var normaliser = new XsvHcdtNormaliser();

        await Assert.ThrowsAsync<XsvValidationException>(() =>
            normaliser.NormaliseAsync(input, output, opts =>
            {
                opts.OutputFormat = OutputFormat.Parquet;
                opts.StrictFieldCount = true;
                opts.InputDelimiter = FieldDelimiter.Pipe;
            }));
    }

    [Fact]
    public async Task NormaliseFileAsync_WhenInputMissing_LeavesExistingOutputFileIntact()
    {
        var inputPath = "does_not_exist.csv";
        var outputPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(outputPath, "Pre-existing critical data");

        var normaliser = new XsvHcdtNormaliser();
        var act = async () => await normaliser.NormaliseFileAsync(inputPath, outputPath);

        await act.Should().ThrowAsync<FileNotFoundException>();

        File.Exists(outputPath).Should().BeTrue();
        var content = await File.ReadAllTextAsync(outputPath);
        content.Should().Be("Pre-existing critical data");

        File.Delete(outputPath);
    }

    [Fact]
    public async Task NormaliseAsync_OnSeekableStream_TruncatesOutputOnValidationFailure()
    {
        using var input = ToStream(FieldCountMismatchHcdt);
        using var output = new MemoryStream();
        output.Write(new byte[100]);

        var normaliser = new XsvHcdtNormaliser();

        await Assert.ThrowsAsync<XsvValidationException>(() =>
            normaliser.NormaliseAsync(input, output, opts =>
            {
                opts.OutputFormat = OutputFormat.Parquet;
                opts.StrictFieldCount = true;
                opts.InputDelimiter = FieldDelimiter.Pipe;
            }));

        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task NormaliseAsync_PreservesColumnAlignmentForKnownValue()
    {
        using var input = ToStream(ValidHcdtWithKnownValue);
        using var output = new MemoryStream();

        var normaliser = new XsvHcdtNormaliser();
        await normaliser.NormaliseAsync(input, output, opts =>
        {
            opts.OutputFormat = OutputFormat.Parquet;
            opts.InputDelimiter = FieldDelimiter.Pipe;
        });

        output.Position = 0;

        await using var reader = await ParquetReader.CreateAsync(output);
        using var rowGroupReader = reader.OpenRowGroupReader(0);

        var dateColumn = reader.Schema.GetDataFields().First(f => f.Name == "ETF_CURRENT_MODIFIED_DATE");

        var dateData = new string[rowGroupReader.RowCount];
        await rowGroupReader.ReadAsync(dateColumn, dateData);

        Assert.Equal("13-OCT-99", dateData[0]);
    }

    [Fact]
    public async Task NormaliseAsync_WithStrictFieldCount_ThrowsOnMismatch()
    {
        using var input = ToStream(FieldCountMismatchHcdt);
        using var output = new MemoryStream();

        var normaliser = new XsvHcdtNormaliser();

        await Assert.ThrowsAsync<XsvValidationException>(() =>
            normaliser.NormaliseAsync(input, output, opts =>
            {
                opts.OutputFormat = OutputFormat.Parquet;
                opts.StrictFieldCount = true;
                opts.InputDelimiter = FieldDelimiter.Pipe;
            }));
    }

    private static Stream ToStream(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));
}