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
            C|ID|NAME
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
        records[0].Fields.Should().BeEquivalentTo("ID", "NAME");

        records[1].Tag.Should().Be('D');
        records[1].Fields.Should().BeEquivalentTo("1", "Alice");

        records[2].Tag.Should().Be('D');
        records[2].Fields.Should().BeEquivalentTo("2", "Bob");
    }
}
