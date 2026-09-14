namespace XsvHcdtHelper.Tests;

using System.Text;
using FluentAssertions;
using Xunit;
using XsvHcdtHelper;

/// <summary>
/// Files that were split after export carry the envelope across the whole set: the first
/// slice opens with H and the last closes with T, so intermediate slices have one, both or
/// neither. RequireHeader / RequireTrailer let those slices be normalised on their own.
/// </summary>
public class OptionalEnvelopeTests
{
    private static async Task<(XsvValidationReport Report, string Csv)> NormaliseAsync(
        string input, Action<XsvHcdtOptions>? configure = null)
    {
        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();

        var report = await new XsvHcdtNormaliser().NormaliseAsync(inputStream, outputStream, configure);

        outputStream.Position = 0;
        var csv = await new StreamReader(outputStream).ReadToEndAsync();
        return (report, csv);
    }

    private const string NoHeader = """
        C|RECORD_TYPE|ID|NAME
        D|100|Alice
        D|101|Bob
        T|PART.csv|23082026 07:00:42|2
        """;

    private const string NoTrailer = """
        H|PART.csv|23082026 07:00:42
        C|RECORD_TYPE|ID|NAME
        D|100|Alice
        D|101|Bob
        """;

    private const string NoHeaderNoTrailer = """
        C|RECORD_TYPE|ID|NAME
        D|100|Alice
        D|101|Bob
        """;

    [Fact]
    public async Task NormaliseAsync_GivenNoHeaderAndDefaults_Throws()
    {
        Func<Task> act = async () => await NormaliseAsync(NoHeader);

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*must start with an 'H'*");
    }

    [Fact]
    public async Task NormaliseAsync_GivenNoTrailerAndDefaults_Throws()
    {
        Func<Task> act = async () => await NormaliseAsync(NoTrailer);

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*missing 'T' (Trailer) record*");
    }

    [Fact]
    public async Task NormaliseAsync_GivenRequireHeaderFalse_ProcessesAFileThatStartsAtTheColumnsRecord()
    {
        var (report, csv) = await NormaliseAsync(NoHeader, o => o.RequireHeader = false);

        report.ActualDataRecords.Should().Be(2);
        report.DeclaredRecordCount.Should().Be(2);
        report.TrailerCountMatched.Should().BeTrue();
        report.FileName.Should().BeNull("there was no header to take a file name from");
        report.Columns.Should().BeEquivalentTo(
            new[] { "RECORD_TYPE", "ID", "NAME" }, opts => opts.WithStrictOrdering());
        csv.Should().Contain("D,100,Alice").And.Contain("D,101,Bob");
    }

    [Fact]
    public async Task NormaliseAsync_GivenRequireTrailerFalse_ProcessesAFileThatEndsAfterTheLastDataRecord()
    {
        var (report, csv) = await NormaliseAsync(NoTrailer, o => o.RequireTrailer = false);

        report.ActualDataRecords.Should().Be(2);
        report.FileName.Should().Be("PART.csv");
        report.DeclaredRecordCount.Should().Be(0, "there was no trailer to declare a count");
        csv.Should().Contain("D,100,Alice").And.Contain("D,101,Bob");
    }

    [Fact]
    public async Task NormaliseAsync_GivenBothOptional_ProcessesAFileWithNoEnvelopeAtAll()
    {
        var (report, csv) = await NormaliseAsync(NoHeaderNoTrailer, o =>
        {
            o.RequireHeader = false;
            o.RequireTrailer = false;
        });

        report.ActualDataRecords.Should().Be(2);
        report.FileName.Should().BeNull();
        csv.Should().Contain("D,100,Alice").And.Contain("D,101,Bob");
    }

    [Fact]
    public async Task NormaliseAsync_GivenRequireTrailerFalseButATrailerIsPresent_StillValidatesIt()
    {
        // Opting out of *requiring* a trailer must not disable checking one that is there.
        const string badCount = """
            H|PART.csv|23082026 07:00:42
            C|RECORD_TYPE|ID|NAME
            D|100|Alice
            T|PART.csv|23082026 07:00:42|99
            """;

        Func<Task> act = async () => await NormaliseAsync(badCount, o => o.RequireTrailer = false);

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*Declared record count (99) does not match actual count (1)*");
    }

    [Fact]
    public async Task NormaliseAsync_GivenRequireHeaderFalseAndAHeaderIsPresent_StillUsesIt()
    {
        const string withHeader = """
            H|PART.csv|23082026 07:00:42
            C|RECORD_TYPE|ID|NAME
            D|100|Alice
            T|PART.csv|23082026 07:00:42|1
            """;

        var (report, _) = await NormaliseAsync(withHeader, o => o.RequireHeader = false);

        report.FileName.Should().Be("PART.csv");
        report.Timestamp.Should().Be("23082026 07:00:42");
        report.HeaderTrailerMatched.Should().BeTrue();
        report.ActualDataRecords.Should().Be(1);
    }

    [Fact]
    public async Task NormaliseAsync_GivenRequireHeaderFalseAndCommaDelimitedInput_StillAutoDetectsTheDelimiter()
    {
        const string input = "C,RECORD_TYPE,ID,NAME\n" +
                             "D,100,Alice\n";

        var (report, csv) = await NormaliseAsync(input, o =>
        {
            o.RequireHeader = false;
            o.RequireTrailer = false;
        });

        report.Columns.Should().BeEquivalentTo(
            new[] { "RECORD_TYPE", "ID", "NAME" }, opts => opts.WithStrictOrdering());
        csv.Should().Contain("D,100,Alice");
    }

    [Fact]
    public async Task NormaliseAsync_GivenNoEnvelopeAndNoColumnsRecord_StillThrows()
    {
        // Column names are not optional: without a C record there is nothing to name the output.
        const string input = """
            D|100|Alice
            """;

        Func<Task> act = async () => await NormaliseAsync(input, o =>
        {
            o.RequireHeader = false;
            o.RequireTrailer = false;
        });

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*before 'C' column definition*");
    }

    [Fact]
    public void XsvHcdtOptions_DefaultsBothEnvelopeRecordsToRequired()
    {
        var options = new XsvHcdtOptions();

        options.RequireHeader.Should().BeTrue();
        options.RequireTrailer.Should().BeTrue();
    }
}
