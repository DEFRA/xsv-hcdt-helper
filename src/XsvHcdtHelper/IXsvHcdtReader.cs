namespace XsvHcdtHelper;

/// <summary>
/// A parsed record. For 'D' records, <see cref="Fields"/> includes the leading tag as its
/// first value (the tag occupies the first declared column, commonly RECORD_TYPE), so the
/// fields align 1:1 with the C column list. For 'C' records, Fields are the column names.
/// </summary>
public sealed record XsvRecord(char Tag, IReadOnlyList<string> Fields);

public interface IXsvHcdtReader
{
    IAsyncEnumerable<XsvRecord> ReadAsync(
        Stream input,
        Action<XsvHcdtOptions>? configure = null,
        CancellationToken ct = default);
}