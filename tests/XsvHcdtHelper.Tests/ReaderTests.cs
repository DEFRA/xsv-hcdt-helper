namespace XsvHcdtHelper.Tests;

using System.Text;
using FluentAssertions;
using Xunit;
using XsvHcdtHelper;

public class ReaderTests
{
    [Fact]
    public async Task ReadAsync_GivenValidInput_YieldsRecordsAndValidates()
    {
        const string input = """
            H|CTSM_UKV.csv|22022026 07:46:03
            C|RECORD_TYPE|ID|NAME
            D|1|Alice
            D|2|Bob
            T|CTSM_UKV.csv|22022026 07:46:03|2
            """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        var reader = new XsvHcdtReader();

        var records = new List<XsvRecord>();

        await foreach (var record in reader.ReadAsync(inputStream, o => o.InputDelimiter = FieldDelimiter.Pipe))
        {
            records.Add(record);
        }

        records.Count.Should().Be(3);

        records[0].Tag.Should().Be('C');
        records[0].Fields.Should().BeEquivalentTo("RECORD_TYPE", "ID", "NAME");

        records[1].Tag.Should().Be('D');
        records[1].Fields.Should().BeEquivalentTo("D", "1", "Alice");

        records[2].Tag.Should().Be('D');
        records[2].Fields.Should().BeEquivalentTo("D", "2", "Bob");
    }

    [Fact]
    public async Task ReadAsync_GivenQuotedPipeFieldWithEmbeddedNewline_YieldsOneDataRecord()
    {
        const string note = "A | value with a \"quote\"\nand an embedded newline";
        var input = "H|CTSM_UKV.csv|22022026 07:46:03\n" +
                    "C|RECORD_TYPE|ID|NOTE\n" +
                    "D|1|\"A | value with a \"\"quote\"\"\nand an embedded newline\"\n" +
                    "T|CTSM_UKV.csv|22022026 07:46:03|1\n";
        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));

        var records = new List<XsvRecord>();
        await foreach (var record in new XsvHcdtReader().ReadAsync(inputStream))
        {
            records.Add(record);
        }

        records.Should().HaveCount(2);
        records[1].Tag.Should().Be('D');
        records[1].Fields.Should().BeEquivalentTo("D", "1", note);
    }
}
