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

        await using var reader = new XsvRfc4180RecordReader(input, options);

        string? headerFileName = null;
        string? headerTimestamp = null;
        long actualDataRecords = 0;
        bool hasReadColumns = false;
        int expectedColumnCount = 0;

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
                var columns = record.Fields.ToList();
                expectedColumnCount = columns.Count;

                yield return new XsvRecord('C', columns);
            }
            else if (record.Tag == 'D')
            {
                if (!hasReadColumns)
                    throw new XsvValidationException("Found 'D' record before 'C' column definition.");

                actualDataRecords++;
                var fields = record.Fields.ToList();

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
            else if (record.Tag == 'T')
            {
                if (options.ValidateEnvelopeOrder && !hasReadColumns)
                {
                    throw new XsvValidationException("File is missing 'C' (Columns) record.");
                }

                var trailerFileName = record.Fields.Count > 0 ? record.Fields[0] : null;
                var trailerTimestamp = record.Fields.Count > 1 ? record.Fields[1] : null;
                var declaredCount = record.Fields.Count > 2 ? long.Parse(record.Fields[2]) : 0;

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
            else
            {
                throw new XsvValidationException($"Unexpected record tag '{record.Tag}' encountered.");
            }
        }

        if (record is null || record.Tag != 'T')
        {
            throw new XsvValidationException("File is missing 'T' (Trailer) record or was truncated.");
        }
    }
}