using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;

namespace XsvHcdtHelper;

public sealed class XsvHcdtReader : IXsvHcdtReader
{
    private readonly XsvHcdtOptions _baseOptions;

    public XsvHcdtReader(IOptions<XsvHcdtOptions>? options = null)
    {
        _baseOptions = options?.Value ?? new XsvHcdtOptions();
    }

    public async IAsyncEnumerable<XsvRecord> ReadAsync(
        Stream input,
        Action<XsvHcdtOptions>? configure = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var options = _baseOptions.Clone();
        configure?.Invoke(options);

        await using var parser = new XsvEnvelopeParser(input, options);
        await foreach (var record in parser.ReadAsync(ct))
        {
            yield return record;
        }
    }
}