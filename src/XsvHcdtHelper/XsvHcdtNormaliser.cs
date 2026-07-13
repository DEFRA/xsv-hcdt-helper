using System;

namespace XsvHcdtHelper;

public sealed class XsvHcdtNormaliser : IXsvHcdtNormaliser
{
    public async Task<XsvValidationReport> NormaliseAsync(
            Stream input,
            Stream output,
            Action<XsvHcdtOptions>? configure = null,
            CancellationToken ct = default)
    {
        var options = new XsvHcdtOptions();
        configure?.Invoke(options);

        try
        {
            await using IRowSink sink = options.OutputFormat == OutputFormat.Csv
                        ? new CsvRowSink(output, options)
                        : new ParquetRowSink(output, options);

            using var reader = new StreamReader(input, leaveOpen: true);

            string? headerFileName = null;
            string? headerTimestamp = null;
            long actualDataRecords = 0;
            bool hasReadColumns = false;
            List<string> columns = new();

            var firstLine = await reader.ReadLineAsync(ct);
            if (firstLine == null || !firstLine.StartsWith("H"))
                throw new XsvValidationException("File must start with an 'H' (Header) record.");

            char delimiter;
            if (options.InputDelimiter == FieldDelimiter.Pipe) delimiter = '|';
            else if (options.InputDelimiter == FieldDelimiter.Comma) delimiter = ',';
            else
            {
                if (firstLine.StartsWith("H|")) delimiter = '|';
                else if (firstLine.StartsWith("H,")) delimiter = ',';
                else throw new XsvValidationException("Cannot auto-detect delimiter. Expected 'H|' or 'H,'.");
            }

            var headerParts = firstLine.Split(delimiter);
            headerFileName = headerParts.Length > 1 ? headerParts[1] : null;
            headerTimestamp = headerParts.Length > 2 ? headerParts[2] : null;

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (line.StartsWith("C"))
                {
                    hasReadColumns = true;
                    var parts = line.Split(delimiter);
                    columns = parts.Skip(1).ToList();
                    sink.Begin(columns);
                }
                else if (line.StartsWith("D"))
                {
                    if (!hasReadColumns)
                        throw new XsvValidationException("Found 'D' record before 'C' column definition.");

                    actualDataRecords++;
                    var parts = line.Split(delimiter);
                    var fields = parts.Skip(1).ToList();

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
                else if (line.StartsWith("T"))
                {
                    var parts = line.Split(delimiter);
                    var trailerFileName = parts.Length > 1 ? parts[1] : null;
                    var trailerTimestamp = parts.Length > 2 ? parts[2] : null;
                    var declaredCount = parts.Length > 3 ? long.Parse(parts[3]) : 0;

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
            }

            if (line == null || !line.StartsWith("T"))
            {
                throw new XsvValidationException("File is missing 'T' (Trailer) record or was truncated.");
            }

            await sink.FinishAsync(ct);

            return new XsvValidationReport(
                headerFileName, headerTimestamp, actualDataRecords, actualDataRecords, true, true, columns);
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
        var options = new XsvHcdtOptions();
        configure?.Invoke(options);

        try
        {
            await using var inputStream = new FileStream(
                inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, options.BufferSize, useAsync: true);

            await using var outputStream = new FileStream(
                outputPath, FileMode.Create, FileAccess.Write, FileShare.None, options.BufferSize, useAsync: true);

            return await NormaliseAsync(inputStream, outputStream, configure, ct);
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
