using System.Runtime.CompilerServices;

namespace XsvHcdtHelper;

/// <summary>
/// Shared streaming H/C/D/T envelope parser used by <see cref="XsvHcdtNormaliser"/> and
/// <see cref="XsvHcdtReader"/>. Enforces envelope structure, validates the trailer against
/// the header and the counted D records, and yields the C record followed by each
/// (field-count normalised) D record. Envelope metadata is populated as parsing passes
/// the relevant record.
/// </summary>
internal sealed class XsvEnvelopeParser : IAsyncDisposable
{
    private readonly XsvRfc4180RecordReader _reader;
    private readonly XsvHcdtOptions _options;
    private List<string> _columns = [];

    public XsvEnvelopeParser(Stream input, XsvHcdtOptions options)
    {
        _options = options;
        _reader = new XsvRfc4180RecordReader(input, options);
    }

    public string? HeaderFileName { get; private set; }
    public string? HeaderTimestamp { get; private set; }
    public string? TrailerFileName { get; private set; }
    public string? TrailerTimestamp { get; private set; }
    public long DeclaredRecordCount { get; private set; }
    public long ActualDataRecords { get; private set; }
    public IReadOnlyList<string> Columns => _columns;

    public async IAsyncEnumerable<XsvRecord> ReadAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var header = await _reader.ReadAsync(ct);
        if (header is null || header.Tag != 'H')
        {
            throw new XsvValidationException("File must start with an 'H' (Header) record.");
        }

        HeaderFileName = header.Fields.Count > 0 ? header.Fields[0] : null;
        HeaderTimestamp = header.Fields.Count > 1 ? header.Fields[1] : null;

        var hasReadColumns = false;
        XsvParsedRecord? record;
        while ((record = await _reader.ReadAsync(ct)) is not null)
        {
            if (record.Tag == 'C')
            {
                if (hasReadColumns)
                {
                    throw new XsvValidationException("Multiple 'C' (Columns) records found.");
                }

                hasReadColumns = true;
                _columns = record.Fields.ToList();
                yield return new XsvRecord('C', _columns);
            }
            else if (record.Tag == 'D')
            {
                if (!hasReadColumns)
                {
                    throw new XsvValidationException("Found 'D' record before 'C' column definition.");
                }

                ActualDataRecords++;
                yield return new XsvRecord('D', NormaliseFieldCount(record.Fields));
            }
            else if (record.Tag == 'T')
            {
                ProcessTrailer(record, hasReadColumns);

                if (await _reader.HasRemainingContentAsync(ct))
                {
                    throw new XsvValidationException(
                        "File must end with exactly one 'T' (Trailer) record; found content after the trailer.");
                }

                yield break;
            }
            else
            {
                throw new XsvValidationException($"Unexpected record tag '{record.Tag}' encountered.");
            }
        }

        throw new XsvValidationException("File is missing 'T' (Trailer) record or was truncated.");
    }

    public ValueTask DisposeAsync() => _reader.DisposeAsync();

    private IReadOnlyList<string> NormaliseFieldCount(IReadOnlyList<string> fields)
    {
        if (fields.Count == _columns.Count)
        {
            return fields;
        }

        if (_options.StrictFieldCount)
        {
            throw new XsvValidationException(
                $"Expected {_columns.Count} fields, but row {ActualDataRecords} had {fields.Count} fields.")
            {
                Expected = _columns.Count.ToString(),
                Actual = fields.Count.ToString(),
            };
        }

        // Pad or truncate to the declared column count.
        var normalised = new List<string>(_columns.Count);
        for (var i = 0; i < _columns.Count; i++)
        {
            normalised.Add(i < fields.Count ? fields[i] : string.Empty);
        }

        return normalised;
    }

    private void ProcessTrailer(XsvParsedRecord trailer, bool hasReadColumns)
    {
        if (_options.ValidateEnvelopeOrder && !hasReadColumns)
        {
            throw new XsvValidationException("File is missing 'C' (Columns) record.");
        }

        TrailerFileName = trailer.Fields.Count > 0 ? trailer.Fields[0] : null;
        TrailerTimestamp = trailer.Fields.Count > 1 ? trailer.Fields[1] : null;

        if (trailer.Fields.Count > 2)
        {
            if (!long.TryParse(trailer.Fields[2], out var declared))
            {
                throw new XsvValidationException(
                    $"Trailer record count '{trailer.Fields[2]}' is not a valid number.")
                {
                    Expected = "a numeric record count",
                    Actual = trailer.Fields[2],
                };
            }

            DeclaredRecordCount = declared;
        }

        if (_options.ValidateTrailerCount && DeclaredRecordCount != ActualDataRecords)
        {
            throw new XsvValidationException(
                $"Declared record count ({DeclaredRecordCount}) does not match actual count ({ActualDataRecords}).")
            {
                Expected = DeclaredRecordCount.ToString(),
                Actual = ActualDataRecords.ToString(),
            };
        }

        if (_options.ValidateHeaderTrailerMatch)
        {
            if (HeaderFileName != TrailerFileName)
            {
                throw new XsvValidationException("Header and Trailer filenames do not match.")
                {
                    Expected = HeaderFileName,
                    Actual = TrailerFileName,
                };
            }

            if (HeaderTimestamp != TrailerTimestamp)
            {
                throw new XsvValidationException("Header and Trailer timestamps do not match.")
                {
                    Expected = HeaderTimestamp,
                    Actual = TrailerTimestamp,
                };
            }
        }
    }
}
