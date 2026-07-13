namespace XsvHcdtHelper;

public interface IXsvHcdtNormaliser
{
    Task<XsvValidationReport> NormaliseAsync(
        Stream input,
        Stream output,
        Action<XsvHcdtOptions>? configure = null,
        CancellationToken ct = default);

    Task<XsvValidationReport> NormaliseFileAsync(
        string inputPath,
        string outputPath,
        Action<XsvHcdtOptions>? configure = null,
        CancellationToken ct = default);
}