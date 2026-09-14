namespace XsvHcdtHelper.Tests;

using System.Text;
using FluentAssertions;
using Xunit;
using XsvHcdtHelper;

public class QuoteHandlingTests
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

    [Fact]
    public async Task NormaliseAsync_GivenRfc4180AndFieldStartingWithQuote_ThrowsBecauseTextFollowsTheClosingQuote()
    {
        // Reproduces a real CTS extract: an unquoted free-text comment column whose value
        // happens to begin with a double quote.
        const string input = """
            H|LEADING_QUOTE.csv|22082026 07:28:26
            C|RECORD_TYPE|ID|COMMENTS
            D|100|"holding closed in Data cleanse exercise".
            T|LEADING_QUOTE.csv|22082026 07:28:26|1
            """;

        Func<Task> act = async () => await NormaliseAsync(input);

        await act.Should().ThrowAsync<XsvValidationException>()
            .WithMessage("*quoted field must be followed by a delimiter*");
    }

    [Fact]
    public async Task NormaliseAsync_GivenQuoteHandlingNone_TreatsALeadingQuoteAsLiteralData()
    {
        const string input = """
            H|LEADING_QUOTE.csv|22082026 07:28:26
            C|RECORD_TYPE|ID|COMMENTS
            D|100|"holding closed in Data cleanse exercise".
            T|LEADING_QUOTE.csv|22082026 07:28:26|1
            """;

        var (report, csv) = await NormaliseAsync(input, o => o.InputQuoting = QuoteHandling.None);

        report.ActualDataRecords.Should().Be(1);
        report.TrailerCountMatched.Should().BeTrue();

        // The output sink re-quotes the value per RFC 4180, so the embedded quotes are doubled.
        csv.Should().Contain("D,100,\"\"\"holding closed in Data cleanse exercise\"\".\"");
    }

    [Fact]
    public async Task NormaliseAsync_GivenQuoteHandlingNone_DoesNotStripSurroundingQuotes()
    {
        const string input = """
            H|WRAPPED.csv|22082026 07:28:26
            C|RECORD_TYPE|ID|NAME
            D|100|"Alice"
            T|WRAPPED.csv|22082026 07:28:26|1
            """;

        var (_, csv) = await NormaliseAsync(input, o => o.InputQuoting = QuoteHandling.None);

        // Rfc4180 would yield Alice; None must preserve the quotes as data.
        csv.Should().Contain("D,100,\"\"\"Alice\"\"\"");
    }

    [Fact]
    public async Task NormaliseAsync_GivenQuoteHandlingNone_TreatsAQuotedDelimiterAsARealDelimiter()
    {
        const string input = """
            H|SPLIT.csv|22082026 07:28:26
            C|RECORD_TYPE|A|B
            D|"x|y"
            T|SPLIT.csv|22082026 07:28:26|1
            """;

        var (report, csv) = await NormaliseAsync(input, o => o.InputQuoting = QuoteHandling.None);

        // With quoting disabled the pipe between the quotes is a real delimiter, so the row
        // splits into three fields (tag + two values).
        report.ActualDataRecords.Should().Be(1);
        csv.Should().Contain("D,\"\"\"x\",\"y\"\"\"");
    }

    [Fact]
    public async Task NormaliseAsync_GivenQuoteHandlingNone_DoesNotLetAnUnbalancedQuoteSwallowFollowingRecords()
    {
        // Under Rfc4180 a quote opened mid-stream can consume the newline and the trailer,
        // producing a truncation error instead of two clean rows.
        const string input = """
            H|UNBALANCED.csv|22082026 07:28:26
            C|RECORD_TYPE|ID|COMMENTS
            D|100|"holding closed in Data cleanse exercise
            D|101|plain text
            T|UNBALANCED.csv|22082026 07:28:26|2
            """;

        var (report, csv) = await NormaliseAsync(input, o => o.InputQuoting = QuoteHandling.None);

        report.ActualDataRecords.Should().Be(2);
        report.TrailerCountMatched.Should().BeTrue();
        csv.Should().Contain("D,101,plain text");
    }

    [Fact]
    public async Task NormaliseAsync_GivenQuoteHandlingNone_StillHonoursAutoDelimiterDetection()
    {
        const string input = "H,AUTO.csv,22082026 07:28:26\n" +
                             "C,RECORD_TYPE,ID,COMMENTS\n" +
                             "D,100,\"note\".\n" +
                             "T,AUTO.csv,22082026 07:28:26,1\n";

        var (report, csv) = await NormaliseAsync(input, o => o.InputQuoting = QuoteHandling.None);

        report.Columns.Should().BeEquivalentTo(
            new[] { "RECORD_TYPE", "ID", "COMMENTS" }, opts => opts.WithStrictOrdering());
        csv.Should().Contain("D,100,\"\"\"note\"\".\"");
    }

    [Fact]
    public async Task NormaliseAsync_GivenQuoteHandlingRfc4180_RemainsTheDefaultAndStillUnquotes()
    {
        const string input = """
            H|QUOTED.csv|22082026 07:28:26
            C|RECORD_TYPE|ID|NAME
            D|100|"Smith, John"
            T|QUOTED.csv|22082026 07:28:26|1
            """;

        var (report, csv) = await NormaliseAsync(input);

        report.ActualDataRecords.Should().Be(1);
        csv.Should().Contain("D,100,\"Smith, John\"");
    }

    [Fact]
    public void XsvHcdtOptions_DefaultsInputQuotingToRfc4180()
    {
        new XsvHcdtOptions().InputQuoting.Should().Be(QuoteHandling.Rfc4180);
    }
}
