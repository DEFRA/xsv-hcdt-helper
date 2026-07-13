namespace XsvHcdtHelper.Tests;

using FluentAssertions;
using Xunit;
using XsvHcdtHelper;
using System.Text;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

public class NormaliserTests
{
    private const string ValidPipeInput = """
        H|CTSM_UKV_PROD_BULK_123456.csv|22022026 07:46:03
        C|RECORD_TYPE|RECORD_COUNT|ETF_ID|ETF_DESCRIPTION
        D|1|3|Pre-Barimo| XXXXXX XXXXX
        D|2|1|Numeric|AA-XXNNNNNNNNNN
        T|CTSM_UKV_PROD_BULK_123456.csv|22022026 07:46:03|2
        """;

    [Fact]
    public async Task NormaliseAsync_GivenValidPipeDelimitedFile_ProducesValidCsvAndReport()
    {
        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(ValidPipeInput));
        using var outputStream = new MemoryStream();

        var normaliser = new XsvHcdtNormaliser();

        var report = await normaliser.NormaliseAsync(inputStream, outputStream, options =>
        {
            options.InputDelimiter = FieldDelimiter.Pipe;
            options.OutputFormat = OutputFormat.Csv;
        });

        report.Should().NotBeNull();
        report.ActualDataRecords.Should().Be(2);
        report.DeclaredRecordCount.Should().Be(2);
        report.TrailerCountMatched.Should().BeTrue();
        report.HeaderTrailerMatched.Should().BeTrue();

        outputStream.Position = 0;
        using var reader = new StreamReader(outputStream);
        var csvOutput = await reader.ReadToEndAsync();

        csvOutput.Should().Contain("RECORD_TYPE,RECORD_COUNT,ETF_ID,ETF_DESCRIPTION");
        csvOutput.Should().Contain("1,3,Pre-Barimo,\" XXXXXX XXXXX\"");
    }

    [Fact]
    public async Task NormaliseAsync_GivenMissingHeader_ThrowsValidationException()
    {
        const string input = """
        C|RECORD_TYPE|RECORD_COUNT
        D|1|3
        T|CTSM_UKV.csv|22022026 07:46:03|1
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        Func<Task> act = async () => await normaliser.NormaliseAsync(inputStream, outputStream);

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*must start with an 'H' (Header) record*");
    }

    [Fact]
    public async Task NormaliseAsync_GivenMissingTrailer_ThrowsValidationException()
    {
        const string input = """
        H|CTSM_UKV.csv|22022026 07:46:03
        C|RECORD_TYPE|RECORD_COUNT
        D|1|3
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        Func<Task> act = async () => await normaliser.NormaliseAsync(inputStream, outputStream);

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*missing 'T' (Trailer) record*");
    }

    [Fact]
    public async Task NormaliseAsync_GivenMismatchedRecordCount_ThrowsValidationException()
    {
        const string input = """
        H|CTSM_UKV.csv|22022026 07:46:03
        C|RECORD_TYPE|RECORD_COUNT
        D|1|3
        D|2|1
        T|CTSM_UKV.csv|22022026 07:46:03|999
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        Func<Task> act = async () => await normaliser.NormaliseAsync(inputStream, outputStream);

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*Declared record count (999) does not match actual count (2)*");
    }

    [Fact]
    public async Task NormaliseAsync_WhenValidationFails_ClearsPartialOutput()
    {
        const string input = """
        H|CTSM_UKV.csv|22022026 07:46:03
        C|RECORD_TYPE|RECORD_COUNT
        D|1|3
        T|CTSM_UKV.csv|22022026 07:46:03|999
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        try
        {
            await normaliser.NormaliseAsync(inputStream, outputStream);
        }
        catch (XsvValidationException)
        {
        }

        outputStream.Length.Should().Be(0);
    }

    [Fact]
    public async Task NormaliseAsync_GivenMismatchedHeaderAndTrailer_ThrowsValidationException()
    {
        const string input = """
            H|CTSM_UKV.csv|22022026 07:46:03
            C|RECORD_TYPE|RECORD_COUNT
            D|1|3
            T|DIFFERENT_FILE.csv|22022026 07:46:03|1
            """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        Func<Task> act = async () => await normaliser.NormaliseAsync(inputStream, outputStream);

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("Header and Trailer filenames do not match.");
    }

    [Fact]
    public async Task NormaliseAsync_GivenMismatchedHeaderAndTrailerTimestamp_ThrowsValidationException()
    {
        const string input = """
            H|CTSM_UKV.csv|22022026 07:46:03
            C|RECORD_TYPE|RECORD_COUNT
            D|1|3
            T|CTSM_UKV.csv|22022026 09:59:59|1
            """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        Func<Task> act = async () => await normaliser.NormaliseAsync(inputStream, outputStream);

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("Header and Trailer timestamps do not match.");
    }

    [Fact]
    public async Task NormaliseAsync_GivenRaggedRowAndStrictFieldCountTrue_ThrowsValidationException()
    {
        const string input = """
            H|CTSM_UKV.csv|22022026 07:46:03
            C|COL1|COL2|COL3
            D|Val1|Val2
            T|CTSM_UKV.csv|22022026 07:46:03|1
            """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        Func<Task> act = async () => await normaliser.NormaliseAsync(inputStream, outputStream, o => o.StrictFieldCount = true);

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*Expected 3 fields, but row 1 had 2 fields*");
    }

    [Fact]
    public async Task NormaliseAsync_GivenRaggedRowAndStrictFieldCountFalse_PadsAndTruncatesCorrectly()
    {
        const string input = """
            H|CTSM_UKV.csv|22022026 07:46:03
            C|COL1|COL2|COL3
            D|Short1|Short2
            D|Long1|Long2|Long3|Long4
            T|CTSM_UKV.csv|22022026 07:46:03|2
            """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        await normaliser.NormaliseAsync(inputStream, outputStream);

        outputStream.Position = 0;
        using var reader = new StreamReader(outputStream);
        var csvOutput = await reader.ReadToEndAsync();

        csvOutput.Should().Contain("Short1,Short2,");
        csvOutput.Should().Contain("Long1,Long2,Long3");
        csvOutput.Should().NotContain("Long4");
    }

    [Fact]
    public async Task NormaliseAsync_GivenValidInput_ProducesValidParquet()
    {
        const string input = """
            H|CTSM_UKV.csv|22022026 07:46:03
            C|ID|NAME|VALUE
            D|1|Alice|100
            D|2|Bob|200
            T|CTSM_UKV.csv|22022026 07:46:03|2
            """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        await normaliser.NormaliseAsync(inputStream, outputStream, o =>
        {
            o.InputDelimiter = FieldDelimiter.Pipe;
            o.OutputFormat = OutputFormat.Parquet;
        });

        outputStream.Position = 0;
        await using var parquetReader = await ParquetReader.CreateAsync(outputStream);

        parquetReader.Schema.Fields.Count.Should().Be(3);

        var dataFields = parquetReader.Schema.GetDataFields();
        dataFields[0].Name.Should().Be("ID");
        dataFields[1].Name.Should().Be("NAME");
        dataFields[2].Name.Should().Be("VALUE");

        using var rowGroupReader = parquetReader.OpenRowGroupReader(0);
        rowGroupReader.RowCount.Should().Be(2);

        var names = new string[rowGroupReader.RowCount];
        await rowGroupReader.ReadAsync(dataFields[1], names);

        names[0].Should().Be("Alice");
        names[1].Should().Be("Bob");
    }

    [Fact]
    public async Task NormaliseAsync_WhenCancelledMidStream_ThrowsAndClearsOutput()
    {
        var sb = new StringBuilder();
        sb.AppendLine("H|CTSM_UKV.csv|22022026 07:46:03");
        sb.AppendLine("C|ID|NAME");
        for (int i = 0; i < 50000; i++)
        {
            sb.AppendLine($"D|{i}|Name{i}");
        }
        sb.AppendLine("T|CTSM_UKV.csv|22022026 07:46:03|50000");

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        using var cts = new CancellationTokenSource();

        cts.CancelAfter(TimeSpan.FromMilliseconds(5));

        Func<Task> act = async () => await normaliser.NormaliseAsync(
            inputStream,
            outputStream,
            o => o.InputDelimiter = FieldDelimiter.Pipe,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        outputStream.Length.Should().Be(0);
    }
}
