namespace XsvHcdtHelper;

public sealed record XsvRecord(char Tag, IReadOnlyList<string> Fields);

public interface IXsvHcdtReader
{
    IAsyncEnumerable<XsvRecord> ReadAsync(
        Stream input,
        Action<XsvHcdtOptions>? configure = null,
        CancellationToken ct = default);
}