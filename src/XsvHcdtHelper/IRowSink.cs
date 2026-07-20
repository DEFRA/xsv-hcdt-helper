namespace XsvHcdtHelper;

/// <summary>
/// Streaming, row-oriented (tabular) sink contract for writing parsed XSV data.
/// Consumers can implement this to pipe H/C/D/T data to custom outputs (e.g. JSON, Database).
/// </summary>
public interface IRowSink : IAsyncDisposable
{
    /// <summary>
    /// Called exactly once when the 'C' (Columns) record is encountered.
    /// </summary>
    /// <param name="columns">The list of column names.</param>
    void Begin(IReadOnlyList<string> columns);

    /// <summary>
    /// Called for every 'D' (Data) record encountered in the stream.
    /// </summary>
    /// <param name="fields">The parsed field values for the row.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask WriteRowAsync(IReadOnlyList<string> fields, CancellationToken ct = default);

    /// <summary>
    /// Called when the 'T' (Trailer) record is successfully reached and validated.
    /// Signals the sink to flush buffers and finalize the file/stream.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    ValueTask FinishAsync(CancellationToken ct = default);
}