using Parquet;
using Parquet.Data;
using Parquet.Schema;
using System.Data;

namespace XsvHcdtHelper;

public sealed class ParquetRowSink : IRowSink
{
    private readonly Stream _output;
    private readonly XsvHcdtOptions _options;

    private ParquetSchema? _schema;
    private ParquetWriter? _writer;

    private DataField<string>[]? _dataFields;
    private List<string>[]? _columnBuffers;
    private int _bufferedRowCount = 0;

    public ParquetRowSink(Stream output, XsvHcdtOptions options)
    {
        _output = output;
        _options = options;
    }

    public void Begin(IReadOnlyList<string> columns)
    {
        _dataFields = columns.Select(c => new DataField<string>(c)).ToArray();
        _schema = new ParquetSchema(_dataFields);

        _columnBuffers = new List<string>[columns.Count];
        for (int i = 0; i < columns.Count; i++)
        {
            _columnBuffers[i] = new List<string>(_options.RowGroupSize);
        }
    }

    public async ValueTask WriteRowAsync(IReadOnlyList<string> fields, CancellationToken ct = default)
    {
        if (_columnBuffers == null) throw new InvalidOperationException("Begin() must be called first.");

        for (int i = 0; i < _columnBuffers.Length; i++)
        {
            _columnBuffers[i].Add(fields[i]);
        }

        _bufferedRowCount++;

        if (_bufferedRowCount >= _options.RowGroupSize)
        {
            await FlushRowGroupAsync(ct);
        }
    }

    public async ValueTask FinishAsync(CancellationToken ct = default)
    {
        if (_bufferedRowCount > 0)
        {
            await FlushRowGroupAsync(ct);
        }

        if (_writer != null)
        {
            await _writer.DisposeAsync();
        }
    }

    private async Task FlushRowGroupAsync(CancellationToken ct)
    {
        if (_schema == null || _columnBuffers == null || _dataFields == null) return;

        if (_writer == null)
        {
            _writer = await ParquetWriter.CreateAsync(_schema, _output, cancellationToken: ct);
        }

        using var rowGroupWriter = _writer.CreateRowGroup();
        for (int i = 0; i < _dataFields.Length; i++)
        {
            await rowGroupWriter.WriteAsync(_dataFields[i], _columnBuffers[i]);

            _columnBuffers[i].Clear();
        }
        _bufferedRowCount = 0;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
