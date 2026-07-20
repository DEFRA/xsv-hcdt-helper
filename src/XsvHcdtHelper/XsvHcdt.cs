namespace XsvHcdtHelper;

/// <summary>
/// Static facade for quick, non-DI usage of the XsvHcdtHelper.
/// </summary>
public static class XsvHcdt
{
    private static readonly XsvHcdtNormaliser _normaliser = new();

    public static Task<XsvValidationReport> NormaliseAsync(
        Stream input,
        Stream output,
        Action<XsvHcdtOptions>? configure = null,
        CancellationToken ct = default)
    {
        return _normaliser.NormaliseAsync(input, output, configure, ct);
    }

    public static Task<XsvValidationReport> NormaliseFileAsync(
        string inputPath,
        string outputPath,
        Action<XsvHcdtOptions>? configure = null,
        CancellationToken ct = default)
    {
        return _normaliser.NormaliseFileAsync(inputPath, outputPath, configure, ct);
    }
}