namespace XsvHcdtHelper.Tests;

using System.Text;
using FluentAssertions;
using Parquet;
using Xunit;
using XsvHcdtHelper;

public class ParquetSinkTests
{
    [Fact]
    public async Task NormaliseAsync_GivenSmallRowGroupSize_WritesMultipleRowGroups()
    {
        // Arrange
        var input = """
            H|FILE.csv|14072026 14:30:00
            C|RECORD_TYPE|ID|NAME
            D|1|Alice
            D|2|Bob
            D|3|Charlie
            D|4|David
            D|5|Eve
            T|FILE.csv|14072026 14:30:00|5
            """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();

        var normaliser = new XsvHcdtNormaliser();

        // Act - Set RowGroupSize to 2.
        await normaliser.NormaliseAsync(inputStream, outputStream, options =>
        {
            options.InputDelimiter = FieldDelimiter.Pipe;
            options.OutputFormat = OutputFormat.Parquet;
            options.RowGroupSize = 2;
        });

        // Assert
        outputStream.Position = 0;
        await using var parquetReader = await ParquetReader.CreateAsync(outputStream);

        parquetReader.RowGroupCount.Should().Be(3, "5 rows split by a max group size of 2 should yield 3 groups.");

        // Check sizes of each group
        using var group0 = parquetReader.OpenRowGroupReader(0);
        group0.RowCount.Should().Be(2);

        using var group1 = parquetReader.OpenRowGroupReader(1);
        group1.RowCount.Should().Be(2);

        using var group2 = parquetReader.OpenRowGroupReader(2);
        group2.RowCount.Should().Be(1);
    }

    [Fact]
    public async Task NormaliseAsync_GivenEmptyFields_WritesEmptyStringsToParquetColumns()
    {
        // Arrange
        var input = """
            H|FILE.csv|14072026 14:30:00
            C|RECORD_TYPE|ID|NAME
            D|1|
            D||Bob
            T|FILE.csv|14072026 14:30:00|2
            """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        // Act
        await normaliser.NormaliseAsync(inputStream, outputStream, options =>
        {
            options.InputDelimiter = FieldDelimiter.Pipe;
            options.OutputFormat = OutputFormat.Parquet;
        });

        // Assert
        outputStream.Position = 0;
        await using var parquetReader = await ParquetReader.CreateAsync(outputStream);
        using var rowGroup = parquetReader.OpenRowGroupReader(0);

        var schemaFields = parquetReader.Schema.GetDataFields();

        // Read the ID column
        var idColumn = new string[2];
        await rowGroup.ReadAsync(schemaFields[1], idColumn);
        idColumn[0].Should().Be("1");
        idColumn[1].Should().Be(""); // Should be safely handled as an empty string

        // Read the NAME column
        var nameColumn = new string[2];
        await rowGroup.ReadAsync(schemaFields[2], nameColumn);
        nameColumn[0].Should().Be("");
        nameColumn[1].Should().Be("Bob");
    }

    [Fact]
    public async Task NormaliseAsync_GivenZeroDataRows_ProducesValidEmptyParquetWithSchema()
    {
        // Arrange - a structurally valid file with H/C/T but no D rows at all
        const string input = """
        H|FILE.csv|14072026 14:30:00
        C|ID|NAME
        T|FILE.csv|14072026 14:30:00|0
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        // Act
        var report = await normaliser.NormaliseAsync(inputStream, outputStream, options =>
        {
            options.InputDelimiter = FieldDelimiter.Pipe;
            options.OutputFormat = OutputFormat.Parquet;
        });

        // Assert
        report.ActualDataRecords.Should().Be(0);
        outputStream.Length.Should().BeGreaterThan(0, "an empty Parquet file still needs schema/footer bytes");

        outputStream.Position = 0;
        await using var parquetReader = await ParquetReader.CreateAsync(outputStream);

        var dataFields = parquetReader.Schema.GetDataFields();
        dataFields.Should().HaveCount(2);
        dataFields[0].Name.Should().Be("ID");
        dataFields[1].Name.Should().Be("NAME");
        parquetReader.RowGroupCount.Should().Be(0);
    }

    [Fact]
    public async Task NormaliseAsync_GivenTrailerMismatchAfterRowGroupFlushed_DisposesWriterCleanlyAndClearsOutput()
    {
        // Arrange
        const string input = """
        H|FILE.csv|14072026 14:30:00
        C|ID|NAME
        D|1|Alice
        D|2|Bob
        T|FILE.csv|14072026 14:30:00|999
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();
        var normaliser = new XsvHcdtNormaliser();

        // Act
        Func<Task> act = async () => await normaliser.NormaliseAsync(inputStream, outputStream, options =>
        {
            options.InputDelimiter = FieldDelimiter.Pipe;
            options.OutputFormat = OutputFormat.Parquet;
            options.RowGroupSize = 1;
        });

        // Assert
        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*Declared record count*");

        outputStream.Length.Should().Be(0, "partial Parquet output must never be left behind on validation failure");
    }

    [Fact]
    public async Task NormaliseAsync_GivenCommaDelimitedInput_ProducesValidParquet()
    {
        const string input = "H,export.csv,14072026 14:35:00\n" +
                              "C,RECORD_TYPE,ID,NOTES\n" +
                              "D,1,\"A note with, a comma\"\n" +
                              "D,2,Simple note\n" +
                              "T,export.csv,14072026 14:35:00,2\n";

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();

        var report = await new XsvHcdtNormaliser().NormaliseAsync(inputStream, outputStream, o =>
        {
            o.InputDelimiter = FieldDelimiter.Comma;
            o.OutputFormat = OutputFormat.Parquet;
        });

        report.ActualDataRecords.Should().Be(2);

        outputStream.Position = 0;
        await using var parquetReader = await ParquetReader.CreateAsync(outputStream);
        var dataFields = parquetReader.Schema.GetDataFields();
        dataFields.Select(f => f.Name).Should().BeEquivalentTo(new[] { "RECORD_TYPE", "ID", "NOTES" }, opts => opts.WithStrictOrdering());

        using var rowGroupReader = parquetReader.OpenRowGroupReader(0);
        var notes = new string[2];
        await rowGroupReader.ReadAsync(dataFields[2], notes);
        notes[0].Should().Be("A note with, a comma");
        notes[1].Should().Be("Simple note");
    }

    [Fact]
    public async Task NormaliseAsync_GivenRaggedRowAndStrictFieldCountTrue_ThrowsForParquetAndClearsOutput()
    {
        const string input = """
        H|CTSM_UKV.csv|22022026 07:46:03
        C|COL1|COL2|COL3
        D|Val1
        T|CTSM_UKV.csv|22022026 07:46:03|1
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();

        Func<Task> act = async () => await new XsvHcdtNormaliser().NormaliseAsync(inputStream, outputStream, o =>
        {
            o.OutputFormat = OutputFormat.Parquet;
            o.StrictFieldCount = true;
        });

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*Expected 3 fields, but row 1 had 2 fields*");

        outputStream.Length.Should().Be(0);
    }

    [Fact]
    public async Task NormaliseAsync_GivenRaggedRowAndStrictFieldCountFalse_PadsAndTruncatesInParquet()
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

        await new XsvHcdtNormaliser().NormaliseAsync(inputStream, outputStream, o => o.OutputFormat = OutputFormat.Parquet);

        outputStream.Position = 0;
        await using var parquetReader = await ParquetReader.CreateAsync(outputStream);
        var dataFields = parquetReader.Schema.GetDataFields();

        using var rowGroupReader = parquetReader.OpenRowGroupReader(0);
        var col3 = new string[2];
        await rowGroupReader.ReadAsync(dataFields[2], col3);

        col3[0].Should().Be("");       // padded - short row had no COL3 value
        col3[1].Should().Be("Long2");  // truncated - Long3 dropped
    }

    [Fact]
    public async Task NormaliseAsync_GivenHeaderTrailerMismatch_ClearsParquetOutput()
    {
        const string input = """
        H|CTSM_UKV.csv|22022026 07:46:03
        C|COL1
        D|Val1
        T|DIFFERENT_FILE.csv|22022026 07:46:03|1
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();

        Func<Task> act = async () => await new XsvHcdtNormaliser().NormaliseAsync(
            inputStream, outputStream, o => o.OutputFormat = OutputFormat.Parquet);

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("Header and Trailer filenames do not match.");

        outputStream.Length.Should().Be(0);
    }

    [Fact]
    public async Task NormaliseAsync_WhenCancelledMidStream_ThrowsAndClearsParquetOutput()
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

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(5));

        Func<Task> act = async () => await new XsvHcdtNormaliser().NormaliseAsync(
            inputStream,
            outputStream,
            o =>
            {
                o.InputDelimiter = FieldDelimiter.Pipe;
                o.OutputFormat = OutputFormat.Parquet;
            },
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        outputStream.Length.Should().Be(0);
    }

    [Fact]
    public async Task ParquetRowSink_DisposeAsync_IsSafeAndIdempotent_AfterMidStreamFailure()
    {
        // Arrange
        using var output = new MemoryStream();
        var options = new XsvHcdtOptions { RowGroupSize = 2 };

        // Act
        await using (var sink = new ParquetRowSink(output, options))
        {
            sink.Begin(new[] { "A", "B" });
            await sink.WriteRowAsync(new[] { "1", "2" });
            await sink.WriteRowAsync(new[] { "3", "4" });
        }

        // Assert
        await using var reopened = new ParquetRowSink(output, options);
    }

    [Fact]
    public async Task ParquetRowSink_DisposeAsync_ClosesWriter_OnAbnormalTermination()
    {
        // Arrange
        using var output = new MemoryStream();
        var options = new XsvHcdtOptions { RowGroupSize = 1 };

        // Act
        await using (var sink = new ParquetRowSink(output, options))
        {
            sink.Begin(new[] { "COL1" });
            await sink.WriteRowAsync(new[] { "VAL1" });
        }

        // Assert
        output.Position = 0;

        var act = async () => await ParquetReader.CreateAsync(output);
        await act.Should().NotThrowAsync("because DisposeAsync should have properly closed the ParquetWriter");
    }
}