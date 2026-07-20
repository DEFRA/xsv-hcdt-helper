namespace XsvHcdtHelper.Tests;

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using XsvHcdtHelper;

public sealed class JsonArrayRowSink : IRowSink
{
    private readonly Utf8JsonWriter _writer;
    private string[] _columns = [];

    public JsonArrayRowSink(Stream output) => _writer = new Utf8JsonWriter(output);

    public void Begin(IReadOnlyList<string> columns)
    {
        _columns = columns.ToArray();
        _writer.WriteStartArray();
    }

    public ValueTask WriteRowAsync(IReadOnlyList<string> fields, CancellationToken ct = default)
    {
        _writer.WriteStartObject();
        for (int i = 0; i < _columns.Length; i++)
        {
            _writer.WriteString(_columns[i], i < fields.Count ? fields[i] : string.Empty);
        }
        _writer.WriteEndObject();
        return ValueTask.CompletedTask;
    }

    public async ValueTask FinishAsync(CancellationToken ct = default)
    {
        _writer.WriteEndArray();
        await _writer.FlushAsync(ct);
    }

    public ValueTask DisposeAsync() => _writer.DisposeAsync();
}

public class CustomSinkTests
{
    [Fact]
    public void AddOutputSink_RegistersCustomSinkTypeInOptions()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddXsvHcdtHelper().AddOutputSink<JsonArrayRowSink>();
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<XsvHcdtOptions>>().Value;
        options.CustomSinkType.Should().Be(typeof(JsonArrayRowSink));
    }

    [Fact]
    public async Task NormaliseAsync_GivenCustomSink_WritesCustomFormat()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddXsvHcdtHelper().AddOutputSink<JsonArrayRowSink>();
        var provider = services.BuildServiceProvider();

        var normaliser = provider.GetRequiredService<IXsvHcdtNormaliser>();

        const string input = """
            H|CTSM_UKV.csv|22022026 07:46:03
            C|RECORD_TYPE|ID|NAME
            D|1|Alice
            D|2|Bob
            T|CTSM_UKV.csv|22022026 07:46:03|2
            """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var outputStream = new MemoryStream();

        // Act
        var report = await normaliser.NormaliseAsync(inputStream, outputStream, o => o.InputDelimiter = FieldDelimiter.Pipe);

        // Assert
        report.ActualDataRecords.Should().Be(2);

        outputStream.Position = 0;
        using var streamReader = new StreamReader(outputStream);
        var jsonOutput = await streamReader.ReadToEndAsync();

        jsonOutput.Should().Be("[{\"RECORD_TYPE\":\"D\",\"ID\":\"1\",\"NAME\":\"Alice\"},{\"RECORD_TYPE\":\"D\",\"ID\":\"2\",\"NAME\":\"Bob\"}]");
    }
}