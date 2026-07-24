namespace XsvHcdtHelper.Tests;

using FluentAssertions;
using Xunit;
using XsvHcdtHelper;
using System.Text;
using CsvHelper;
using System.Globalization;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using XsvHcdtHelper.Tests.Helpers;

public class NormaliserTests
{
    private const string ValidPipeInput = """
        H|CTSM_UKV_PROD_BULK_123456.csv|22022026 07:46:03
        C|RECORD_TYPE|RECORD_COUNT|ETF_ID|ETF_DESCRIPTION|ETF_FORMAT_PATTERN
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

        csvOutput.Should().Contain("RECORD_TYPE,RECORD_COUNT,ETF_ID,ETF_DESCRIPTION,ETF_FORMAT_PATTERN");
        csvOutput.Should().Contain("D,1,3,Pre-Barimo,\" XXXXXX XXXXX\"");
    }

    [Fact]
    public async Task NormaliseAsync_GivenQuotedCommaRecordWithEmbeddedNewline_PreservesLogicalField()
    {
        const string notes = "A comma, a pipe |, and a \"quote\"\nacross two lines";
        var input = "H,export.csv,22022026 07:46:03\n" +
                    "C,RECORD_TYPE,ID,NOTES\n" +
                    "D,1,\"A comma, a pipe |, and a \"\"quote\"\"\nacross two lines\"\n" +
                    "T,export.csv,22022026 07:46:03,1\n";

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();

        var report = await new XsvHcdtNormaliser().NormaliseAsync(inputStream, outputStream);

        report.ActualDataRecords.Should().Be(1);
        outputStream.Position = 0;
        using var csv = new CsvReader(new StreamReader(outputStream), CultureInfo.InvariantCulture);
        csv.Read();
        csv.ReadHeader();
        csv.Read();
        csv.GetField("NOTES").Should().Be(notes);
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
            D|Val1
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
            D|Short1
            D|Long1|Long2|Long3
            T|CTSM_UKV.csv|22022026 07:46:03|2
            """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        await normaliser.NormaliseAsync(inputStream, outputStream);

        outputStream.Position = 0;
        using var reader = new StreamReader(outputStream);
        var csvOutput = await reader.ReadToEndAsync();

        csvOutput.Should().Contain("D,Short1,");
        csvOutput.Should().Contain("D,Long1,Long2");
        csvOutput.Should().NotContain("Long3");
    }

    [Fact]
    public async Task NormaliseAsync_GivenValidInput_ProducesValidParquet()
    {
        const string input = """
            H|CTSM_UKV.csv|22022026 07:46:03
            C|RECORD_TYPE|ID|NAME|VALUE
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

        parquetReader.Schema.Fields.Count.Should().Be(4);

        var dataFields = parquetReader.Schema.GetDataFields();
        dataFields[0].Name.Should().Be("RECORD_TYPE");
        dataFields[1].Name.Should().Be("ID");
        dataFields[2].Name.Should().Be("NAME");
        dataFields[3].Name.Should().Be("VALUE");

        using var rowGroupReader = parquetReader.OpenRowGroupReader(0);
        rowGroupReader.RowCount.Should().Be(2);

        var names = new string[rowGroupReader.RowCount];
        await rowGroupReader.ReadAsync(dataFields[2], names);

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

    [Fact]
    public async Task NormaliseFileAsync_WhenValidationFails_ReleasesLockAndDeletesOutputFile()
    {
        // Arrange
        var inputPath = Path.GetTempFileName();
        var outputPath = Path.GetTempFileName();

        // Write explicitly invalid data (missing the 'T' trailer) to force a validation exception
        const string invalidInput = """
            H|CTSM_UKV.csv|22022026 07:46:03
            C|RECORD_TYPE|RECORD_COUNT
            D|1|3
            """;
        await File.WriteAllTextAsync(inputPath, invalidInput);

        var normaliser = new XsvHcdtNormaliser();

        try
        {
            // Act
            Func<Task> act = async () => await normaliser.NormaliseFileAsync(inputPath, outputPath);

            // Assert
            // Check that the correct exception is thrown
            await act.Should().ThrowAsync<XsvValidationException>()
                .WithMessage("*missing 'T' (Trailer) record*");

            // Ensure the file was successfully deleted
            File.Exists(outputPath).Should().BeFalse(
                "The output file should have been deleted during the catch block cleanup.");
        }
        finally
        {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task NormaliseAsync_GivenDataRecordBeforeColumnsRecord_ThrowsValidationException()
    {
        const string input = """
        H|CTSM_UKV.csv|14072026 14:30:00
        D|1|Alice
        C|ID|NAME
        T|CTSM_UKV.csv|14072026 14:30:00|1
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        Func<Task> act = async () => await normaliser.NormaliseAsync(inputStream, outputStream);

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*Found 'D' record before 'C' column definition*");
    }

    [Fact]
    public async Task NormaliseAsync_GivenNoColumnsRecordAtAll_ThrowsValidationException()
    {
        const string input = """
        H|CTSM_UKV.csv|14072026 14:30:00
        T|CTSM_UKV.csv|14072026 14:30:00|0
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        Func<Task> act = async () => await normaliser.NormaliseAsync(inputStream, outputStream);

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*missing 'C' (Columns) record*");
    }

    [Fact]
    public async Task NormaliseAsync_GivenEmptyInput_ThrowsValidationException()
    {
        using var inputStream = new MemoryStream();
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        Func<Task> act = async () => await normaliser.NormaliseAsync(inputStream, outputStream);

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*must start with an 'H' (Header) record*");
    }

    [Fact]
    public async Task NormaliseAsync_GivenFileStartingWithTrailerRecord_ThrowsValidationException()
    {
        // Mirrors test-files/invalid_t_at_top.psv
        const string input = "T|CTSM_UKV.csv|14072026 14:30:00|0\n";

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        Func<Task> act = async () => await normaliser.NormaliseAsync(inputStream, outputStream);

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*must start with an 'H' (Header) record*");
    }

    [Fact]
    public async Task NormaliseAsync_GivenValidInput_ReportExposesResolvedColumnList()
    {
        const string input = """
        H|CTSM_UKV.csv|22022026 07:46:03
        C|RECORD_TYPE|ID|NAME
        D|1001|Alice
        T|CTSM_UKV.csv|22022026 07:46:03|1
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        var report = await normaliser.NormaliseAsync(inputStream, outputStream);

        report.Columns.Should().BeEquivalentTo(
            new[] { "RECORD_TYPE", "ID", "NAME" },
            opts => opts.WithStrictOrdering());
    }

    [Fact]
    public async Task NormaliseAsync_GivenTicketWorkedExample_KeepsRecordTagAsFirstColumnValue()
    {
        // Golden-file test: the canonical eartag-formats extract from the story. The C record
        // declares 12 columns; each D row *including* its leading tag has 12 fields, with the
        // tag occupying RECORD_TYPE. StrictFieldCount=true proves the alignment is exact.
        const string input =
            "H|CTSM_UKV_PROD_BULK_######_CT_EARTAG_FORMATS_2026-02-22-074603.csv|22022026 07:46:03\n" +
            "C|RECORD_TYPE|RECORD_COUNT|ETF_ID|ETF_DESCRIPTION|ETF_FORMAT_PATTERN|ETF_MAX_INPUT_LENGTH|ETF_EXTRA_CHARS_ALLOWED|ETF_CURRENT_USER|ETF_CURRENT_STATUS|ETF_CURRENT_MODIFIED_DATE|ETF_CURRENT_PID|ETF_VERSION\n" +
            "D|1|3|Pre-Barimo|  XXXXXX XXXXX|14||x901189|1|13-OCT-99|1|1\n" +
            "D|2|1|Numeric|AA-XXNNNNNNNNNN|14||x903916|1|21-JUL-99|1|1\n" +
            "D|3|4|Free Text|XXXXXXXXXXXXXX|14||x901187|1|13-MAY-99|1|1\n" +
            "D|4|2|Barimo|AAXXNNNN NNNNN|14||x901187|1|13-MAY-99|1|1\n" +
            "T|CTSM_UKV_PROD_BULK_######_CT_EARTAG_FORMATS_2026-02-22-074603.csv|22022026 07:46:03|4\n";

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();

        var report = await new XsvHcdtNormaliser().NormaliseAsync(
            inputStream, outputStream, o => o.StrictFieldCount = true);

        report.ActualDataRecords.Should().Be(4);
        report.DeclaredRecordCount.Should().Be(4);
        report.Columns.Should().HaveCount(12);
        report.Columns[0].Should().Be("RECORD_TYPE");

        outputStream.Position = 0;
        var lines = (await new StreamReader(outputStream).ReadToEndAsync())
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines.Should().HaveCount(5);
        lines[0].Should().Be("RECORD_TYPE,RECORD_COUNT,ETF_ID,ETF_DESCRIPTION,ETF_FORMAT_PATTERN,ETF_MAX_INPUT_LENGTH,ETF_EXTRA_CHARS_ALLOWED,ETF_CURRENT_USER,ETF_CURRENT_STATUS,ETF_CURRENT_MODIFIED_DATE,ETF_CURRENT_PID,ETF_VERSION");
        lines[1].Should().Be("D,1,3,Pre-Barimo,\"  XXXXXX XXXXX\",14,,x901189,1,13-OCT-99,1,1");
        lines[2].Should().Be("D,2,1,Numeric,AA-XXNNNNNNNNNN,14,,x903916,1,21-JUL-99,1,1");
        lines[3].Should().Be("D,3,4,Free Text,XXXXXXXXXXXXXX,14,,x901187,1,13-MAY-99,1,1");
        lines[4].Should().Be("D,4,2,Barimo,AAXXNNNN NNNNN,14,,x901187,1,13-MAY-99,1,1");
    }

    [Fact]
    public async Task NormaliseAsync_GivenNonNumericTrailerCount_ThrowsValidationException()
    {
        const string input = """
        H|CTSM_UKV.csv|22022026 07:46:03
        C|RECORD_TYPE|RECORD_COUNT
        D|1
        T|CTSM_UKV.csv|22022026 07:46:03|abc
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();

        Func<Task> act = async () => await new XsvHcdtNormaliser().NormaliseAsync(inputStream, outputStream);

        var ex = await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*Trailer record count 'abc' is not a valid number*");
        ex.Which.Actual.Should().Be("abc");
        outputStream.Length.Should().Be(0);
    }

    [Fact]
    public async Task NormaliseAsync_GivenTrailerCountTooLow_ThrowsValidationExceptionWithContext()
    {
        const string input = """
        H|CTSM_UKV.csv|22022026 07:46:03
        C|RECORD_TYPE|RECORD_COUNT
        D|1
        D|2
        T|CTSM_UKV.csv|22022026 07:46:03|1
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();

        Func<Task> act = async () => await new XsvHcdtNormaliser().NormaliseAsync(inputStream, outputStream);

        var ex = await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*Declared record count (1) does not match actual count (2)*");
        ex.Which.Expected.Should().Be("1");
        ex.Which.Actual.Should().Be("2");
    }

    [Fact]
    public async Task NormaliseAsync_GivenContentAfterTrailer_ThrowsValidationExceptionAndClearsOutput()
    {
        const string input = """
        H|CTSM_UKV.csv|22022026 07:46:03
        C|RECORD_TYPE|RECORD_COUNT
        D|1
        T|CTSM_UKV.csv|22022026 07:46:03|1
        D|9
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();

        Func<Task> act = async () => await new XsvHcdtNormaliser().NormaliseAsync(inputStream, outputStream);

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*content after the trailer*");
        outputStream.Length.Should().Be(0);
    }

    [Fact]
    public async Task NormaliseAsync_GivenTrailingBlankLinesAfterTrailer_Succeeds()
    {
        const string input = "H|CTSM_UKV.csv|22022026 07:46:03\n" +
                             "C|RECORD_TYPE|RECORD_COUNT\n" +
                             "D|1\n" +
                             "T|CTSM_UKV.csv|22022026 07:46:03|1\n\n\n";

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();

        var report = await new XsvHcdtNormaliser().NormaliseAsync(inputStream, outputStream);

        report.ActualDataRecords.Should().Be(1);
    }

    [Fact]
    public async Task NormaliseAsync_GivenNonSeekableOutput_ValidationFailureIsNotMasked()
    {
        const string input = """
    H|CTSM_UKV.csv|22022026 07:46:03
    C|RECORD_TYPE|RECORD_COUNT
    D|1
    T|CTSM_UKV.csv|22022026 07:46:03|999
    """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));

        await using var outputStream = new NonSeekableWriteStream(new MemoryStream());

        Func<Task> act = async () => await new XsvHcdtNormaliser().NormaliseAsync(inputStream, outputStream);

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*Declared record count*");
    }

    [Fact]
    public async Task NormaliseFileAsync_WhenInputFileMissing_LeavesPreExistingOutputFileIntact()
    {
        var missingInputPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var outputPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(outputPath, "precious output from an earlier run");

        try
        {
            Func<Task> act = async () => await new XsvHcdtNormaliser().NormaliseFileAsync(missingInputPath, outputPath);

            await act.Should().ThrowAsync<FileNotFoundException>();

            File.Exists(outputPath).Should().BeTrue("a file this call never wrote to must not be deleted");
            (await File.ReadAllTextAsync(outputPath)).Should().Be("precious output from an earlier run");
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task NormaliseFileAsync_WhenCancelledMidStream_DeletesOutputFile()
    {
        var inputPath = Path.GetTempFileName();
        var outputPath = Path.GetTempFileName();

        try
        {
            await using (var writer = new StreamWriter(inputPath))
            {
                await writer.WriteLineAsync("H|CTSM_UKV.csv|22022026 07:46:03");
                await writer.WriteLineAsync("C|RECORD_TYPE|ID|NAME");
                for (int i = 0; i < 200_000; i++)
                {
                    await writer.WriteLineAsync($"D|{i}|Name{i}");
                }
                await writer.WriteLineAsync("T|CTSM_UKV.csv|22022026 07:46:03|200000");
            }

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMilliseconds(5));

            Func<Task> act = async () => await new XsvHcdtNormaliser().NormaliseFileAsync(
                inputPath, outputPath, ct: cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();

            File.Exists(outputPath).Should().BeFalse("a cancelled run must not leave a partial output file");
        }
        finally
        {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task NormaliseAsync_GivenFieldLargerThanBufferSize_HandlesCorrectly()
    {
        var largeValue = new string('A', 100_000); // 100KB string, larger than default 64KB buffer
        var input = $"H|f.csv|d\nC|TAG|DATA\nD|{largeValue}\nT|f.csv|d|1\n";

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();

        // Use a small buffer to force multiple sreads
        await new XsvHcdtNormaliser().NormaliseAsync(inputStream, outputStream, o => o.BufferSize = 1024);

        outputStream.Position = 0;
        var csv = await new StreamReader(outputStream).ReadToEndAsync();
        csv.Should().Contain(largeValue);
    }

    [Fact]
    public async Task NormaliseAsync_GivenUtf8WithBom_DetectsHeaderCorrectly()
    {
        var encodingWithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        const string content = "H|f.csv|d\nC|COL\nD|Val\nT|f.csv|d|1\n";

        using var inputStream = new MemoryStream(encodingWithBom.GetPreamble().Concat(Encoding.UTF8.GetBytes(content)).ToArray());
        using var outputStream = new MemoryStream();

        var report = await new XsvHcdtNormaliser().NormaliseAsync(inputStream, outputStream);
        report.ActualDataRecords.Should().Be(1);
    }

    [Fact]
    public async Task NormaliseAsync_WhenParquetFailsAfterFirstRowGroupFlush_ClearsSeekableOutput()
    {
        const string input = "H|f.csv|d\nC|COL\nD|1\nD|2\nD|3\nT|f.csv|d|999\n";

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();

        var act = async () => await new XsvHcdtNormaliser().NormaliseAsync(inputStream, outputStream, o => {
            o.OutputFormat = OutputFormat.Parquet;
            o.RowGroupSize = 2;
        });

        await act.Should().ThrowAsync<XsvValidationException>();
        outputStream.Length.Should().Be(0, "The stream should be truncated even if row groups were already written");
    }

    [Fact]
    public async Task NormaliseAsync_HandlesComplexQuotingAndEmbeddedDelimiters()
    {
        // Field 2 contains: a quote, a pipe, a comma, and a newline.
        const string complexValue = "Value with \"quote\", | pipe, and \n newline";
        var input = "H|f.csv|d\nC|TAG|COL1\nD|\"Value with \"\"quote\"\", | pipe, and \n newline\"\nT|f.csv|d|1\n";

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();

        await new XsvHcdtNormaliser().NormaliseAsync(inputStream, outputStream);

        outputStream.Position = 0;
        using var reader = new StreamReader(outputStream);
        var output = await reader.ReadToEndAsync();

        // CSV output should escape the newline and quotes correctly
        output.Should().Contain("\"Value with \"\"quote\"\", | pipe, and \n newline\"");
    }
}
