using System.Runtime.CompilerServices;

namespace XsvHcdtHelper;

public sealed class XsvHcdtReader : IXsvHcdtReader
{
    public async IAsyncEnumerable<XsvRecord> ReadAsync(
        Stream input,
        Action<XsvHcdtOptions>? configure = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var options = new XsvHcdtOptions();
        configure?.Invoke(options);

        using var streamReader = new StreamReader(input, leaveOpen: true);

        string? headerFileName = null;
        string? headerTimestamp = null;
        long actualDataRecords = 0;
        bool hasReadColumns = false;
        int expectedColumnCount = 0;

        var firstLine = await streamReader.ReadLineAsync(ct);
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
        while ((line = await streamReader.ReadLineAsync(ct)) != null)
        {
            if (line.StartsWith("C"))
            {
                hasReadColumns = true;
                var parts = line.Split(delimiter);
                var columns = parts.Skip(1).ToList();
                expectedColumnCount = columns.Count;

                yield return new XsvRecord('C', columns);
            }
            else if (line.StartsWith("D"))
            {
                if (!hasReadColumns)
                    throw new XsvValidationException("Found 'D' record before 'C' column definition.");

                actualDataRecords++;
                var parts = line.Split(delimiter);
                var fields = parts.Skip(1).ToList();

                if (options.StrictFieldCount && fields.Count != expectedColumnCount)
                {
                    throw new XsvValidationException(
                        $"Expected {expectedColumnCount} fields, but row {actualDataRecords} had {fields.Count} fields.");
                }
                else if (!options.StrictFieldCount && fields.Count != expectedColumnCount)
                {
                    if (fields.Count > expectedColumnCount)
                    {
                        fields = fields.Take(expectedColumnCount).ToList();
                    }
                    else
                    {
                        var missingCount = expectedColumnCount - fields.Count;
                        fields.AddRange(Enumerable.Repeat(string.Empty, missingCount));
                    }
                }

                yield return new XsvRecord('D', fields);
            }
            else if (line.StartsWith("T"))
            {
                var parts = line.Split(delimiter);
                var trailerFileName = parts.Length > 1 ? parts[1] : null;
                var trailerTimestamp = parts.Length > 2 ? parts[2] : null;
                var declaredCount = parts.Length > 3 ? long.Parse(parts[3]) : 0;

                if (options.ValidateTrailerCount && declaredCount != actualDataRecords)
                    throw new XsvValidationException($"Declared record count ({declaredCount}) does not match actual count ({actualDataRecords}).");

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
    }
}
