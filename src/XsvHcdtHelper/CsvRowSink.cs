using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace XsvHcdtHelper;

public sealed class CsvRowSink : IRowSink
{
    private readonly StreamWriter _streamWriter;
    private readonly CsvWriter _csvWriter;

    public CsvRowSink(Stream output, XsvHcdtOptions options)
    {
        _streamWriter = new StreamWriter(output, leaveOpen: true);

        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ",",
        };
        _csvWriter = new CsvWriter(_streamWriter, csvConfig);
    }

    public void Begin(IReadOnlyList<string> columns)
    {
        foreach (var col in columns)
        {
            _csvWriter.WriteField(col);
        }
        _csvWriter.NextRecord();
    }

    public async ValueTask WriteRowAsync(IReadOnlyList<string> fields, CancellationToken ct = default)
    {
        foreach (var field in fields)
        {
            _csvWriter.WriteField(field);
        }
        await _csvWriter.NextRecordAsync();
    }

    public async ValueTask FinishAsync(CancellationToken ct = default)
    {
        await _streamWriter.FlushAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _csvWriter.DisposeAsync();
        await _streamWriter.DisposeAsync();
    }
}
