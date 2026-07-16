using System;
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

            await using var reader = new XsvRfc4180RecordReader(input, options);

            string? headerFileName = null;
            string? headerTimestamp = null;
            string? trailerFileName = null;
            string? trailerTimestamp = null;
            long declaredCount = 0;
            long actualDataRecords = 0;
            bool hasReadColumns = false;
            List<string> columns = new();

            var header = await reader.ReadAsync(ct);
            if (header is null || header.Tag != 'H')
                throw new XsvValidationException("File must start with an 'H' (Header) record.");

            headerFileName = header.Fields.Count > 0 ? header.Fields[0] : null;
            headerTimestamp = header.Fields.Count > 1 ? header.Fields[1] : null;

            XsvParsedRecord? record;
            while ((record = await reader.ReadAsync(ct)) is not null)
            {
                if (record.Tag == 'C')
                {
                    if (hasReadColumns)
                        throw new XsvValidationException("Multiple 'C' (Columns) records found.");

                    hasReadColumns = true;
                    columns = record.Fields.ToList();
                    sink.Begin(columns);
                }
                else if (record.Tag == 'D')
                {
                    if (!hasReadColumns)
                        throw new XsvValidationException("Found 'D' record before 'C' column definition.");

                    actualDataRecords++;
                    var fields = record.Fields.ToList();

                    if (options.StrictFieldCount && fields.Count != columns.Count)
                    {
                        throw new XsvValidationException(
                            $"Expected {columns.Count} fields, but row {actualDataRecords} had {fields.Count} fields.");
                    }
                    else if (!options.StrictFieldCount && fields.Count != columns.Count)
                    {
                        if (fields.Count > columns.Count)
                        {
                            fields = fields.Take(columns.Count).ToList();
                        }
                        else
                        {
                            var missingCount = columns.Count - fields.Count;
                            fields.AddRange(Enumerable.Repeat(string.Empty, missingCount));
                        }
                    }

                    await sink.WriteRowAsync(fields, ct);
                }
                else if (record.Tag == 'T')
                {
                    if (options.ValidateEnvelopeOrder && !hasReadColumns)
                    {
                        throw new XsvValidationException("File is missing 'C' (Columns) record.");
                    }

                    trailerFileName = record.Fields.Count > 0 ? record.Fields[0] : null;
                    trailerTimestamp = record.Fields.Count > 1 ? record.Fields[1] : null;
                    declaredCount = record.Fields.Count > 2 ? long.Parse(record.Fields[2]) : 0;

                    if (options.ValidateTrailerCount && declaredCount != actualDataRecords)
                    {
                        throw new XsvValidationException(
                            $"Declared record count ({declaredCount}) does not match actual count ({actualDataRecords}).");
                    }

                    if (options.ValidateHeaderTrailerMatch)
                    {
                        if (headerFileName != trailerFileName)
                            throw new XsvValidationException("Header and Trailer filenames do not match.");

                        if (headerTimestamp != trailerTimestamp)
                            throw new XsvValidationException("Header and Trailer timestamps do not match.");
                    }

                    break;
                }
                else
                {
                    throw new XsvValidationException($"Unexpected record tag '{record.Tag}' encountered.");
                }
            }

            if (record is null || record.Tag != 'T')
            {
                throw new XsvValidationException("File is missing 'T' (Trailer) record or was truncated.");
            }

            await sink.FinishAsync(ct);

            bool trailerMatched = declaredCount == actualDataRecords;
            bool headerMatched = (headerFileName == trailerFileName) && (headerTimestamp == trailerTimestamp);

            return new XsvValidationReport(
                headerFileName, headerTimestamp, declaredCount, actualDataRecords, trailerMatched, headerMatched, columns);
        }
        catch (Exception)
        {
            output.SetLength(0);
            throw;
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

        try
        {
            await using (var inputStream = new FileStream(
                inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, options.BufferSize, useAsync: true))
            await using (var outputStream = new FileStream(
                outputPath, FileMode.Create, FileAccess.Write, FileShare.None, options.BufferSize, useAsync: true))
            {
                return await NormaliseAsync(inputStream, outputStream, configure, ct);
            }
        }
        catch (Exception)
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
            throw;
        }
    }
}