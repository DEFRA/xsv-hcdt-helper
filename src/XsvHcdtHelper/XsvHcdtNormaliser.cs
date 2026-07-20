using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace XsvHcdtHelper;

public sealed class XsvHcdtNormaliser : IXsvHcdtNormaliser
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly XsvHcdtOptions _baseOptions;
    public XsvHcdtNormaliser(IServiceProvider? serviceProvider = null, IOptions<XsvHcdtOptions>? options = null)
    {
        _serviceProvider = serviceProvider;
        _baseOptions = options?.Value ?? new XsvHcdtOptions();
    }

    private IRowSink CreateSink(Stream output, XsvHcdtOptions options)
    {
        if (options.CustomSinkType != null)
        {
            if (_serviceProvider != null)
            {
                return (IRowSink)ActivatorUtilities.CreateInstance(_serviceProvider, options.CustomSinkType, output);
            }

            return (IRowSink)Activator.CreateInstance(options.CustomSinkType, output)!;
        }

        return options.OutputFormat == OutputFormat.Csv
            ? new CsvRowSink(output, options)
            : new ParquetRowSink(output, options);
    }

    public async Task<XsvValidationReport> NormaliseAsync(
            Stream input,
            Stream output,
            Action<XsvHcdtOptions>? configure = null,
            CancellationToken ct = default)
    {
        var options = _baseOptions.Clone();
        configure?.Invoke(options);

        try
        {
            await using IRowSink sink = CreateSink(output, options);
            await using var parser = new XsvEnvelopeParser(input, options);

            await foreach (var record in parser.ReadAsync(ct))
            {
                if (record.Tag == 'C')
                {
                    sink.Begin(record.Fields);
                }
                else
                {
                    await sink.WriteRowAsync(record.Fields, ct);
                }
            }

            await sink.FinishAsync(ct);

            return new XsvValidationReport(
                parser.HeaderFileName,
                parser.HeaderTimestamp,
                parser.DeclaredRecordCount,
                parser.ActualDataRecords,
                parser.DeclaredRecordCount == parser.ActualDataRecords,
                parser.HeaderFileName == parser.TrailerFileName && parser.HeaderTimestamp == parser.TrailerTimestamp,
                parser.Columns);
        }
        catch (Exception)
        {
            TryClearPartialOutput(output);
            throw;
        }
    }

    /// <summary>
    /// Best-effort removal of partially written output. Non-seekable streams cannot be
    /// truncated (callers own downstream cleanup), and cleanup failures must never mask
    /// the original exception.
    /// </summary>
    private static void TryClearPartialOutput(Stream output)
    {
        if (!output.CanSeek)
        {
            return;
        }

        try
        {
            output.SetLength(0);
        }
        catch
        {
            // Swallow: the original failure is being rethrown.
        }
    }

    public async Task<XsvValidationReport> NormaliseFileAsync(
            string inputPath,
            string outputPath,
            Action<XsvHcdtOptions>? configure = null,
            CancellationToken ct = default)
    {
        var options = _baseOptions.Clone();
        configure?.Invoke(options);

        // Open the input first: if it fails, no output file has been created or touched.
        await using var inputStream = new FileStream(
            inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, options.BufferSize, useAsync: true);

        var outputCreated = false;
        try
        {
            await using (var outputStream = new FileStream(
                outputPath, FileMode.Create, FileAccess.Write, FileShare.None, options.BufferSize, useAsync: true))
            {
                outputCreated = true;
                return await NormaliseAsync(inputStream, outputStream, configure, ct);
            }
        }
        catch when (outputCreated)
        {
            // Only delete a file this call actually created/truncated.
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
            throw;
        }
    }
}