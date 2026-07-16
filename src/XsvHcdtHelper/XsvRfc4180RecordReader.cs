using System.Text;

namespace XsvHcdtHelper;

/// <summary>
/// Reads one RFC 4180 record at a time without loading the input stream into memory.
/// The first H record establishes the delimiter when <see cref="FieldDelimiter.Auto"/> is used.
/// </summary>
internal sealed class XsvRfc4180RecordReader : IAsyncDisposable
{
    private readonly StreamReader _reader;
    private readonly char[] _buffer;
    private readonly FieldDelimiter _configuredDelimiter;

    private int _bufferPosition;
    private int _bufferLength;
    private char? _unreadCharacter;
    private bool _discardLeadingLineFeed;
    private char? _delimiter;

    public XsvRfc4180RecordReader(Stream input, XsvHcdtOptions options)
    {
        _configuredDelimiter = options.InputDelimiter;
        _reader = new StreamReader(
            input,
            encoding: Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: options.BufferSize,
            leaveOpen: true);
        _buffer = new char[Math.Max(1024, options.BufferSize)];
    }

    public async ValueTask<XsvParsedRecord?> ReadAsync(CancellationToken ct)
    {
        var rawRecord = await ReadRawRecordAsync(ct);
        if (rawRecord is null)
        {
            return null;
        }

        if (_delimiter is null)
        {
            _delimiter = ResolveDelimiter(rawRecord);
        }

        var fields = ParseFields(rawRecord, _delimiter.Value);
        if (fields.Count == 0 || fields[0].Length != 1)
        {
            throw new XsvValidationException("Each record must start with a single-character H, C, D, or T tag.");
        }

        return new XsvParsedRecord(fields[0][0], fields.Skip(1).ToArray());
    }

    public ValueTask DisposeAsync()
    {
        _reader.Dispose();
        return ValueTask.CompletedTask;
    }

    private char ResolveDelimiter(string firstRecord)
    {
        if (_configuredDelimiter == FieldDelimiter.Pipe)
        {
            return '|';
        }

        if (_configuredDelimiter == FieldDelimiter.Comma)
        {
            return ',';
        }

        if (firstRecord.Length > 1 && firstRecord[1] == '|')
        {
            return '|';
        }

        if (firstRecord.Length > 1 && firstRecord[1] == ',')
        {
            return ',';
        }

        throw new XsvValidationException("Cannot auto-detect delimiter. Expected 'H|' or 'H,'.");
    }

    private async ValueTask<string?> ReadRawRecordAsync(CancellationToken ct)
    {
        var record = new StringBuilder();
        var inQuotedField = false;
        var quotePending = false;
        var atFieldStart = true;
        var readAnyCharacter = false;

        while (true)
        {
            char character;
            if (_unreadCharacter is { } unreadCharacter)
            {
                _unreadCharacter = null;
                character = unreadCharacter;
            }
            else
            {
                if (_bufferPosition == _bufferLength)
                {
                    _bufferLength = await _reader.ReadAsync(_buffer.AsMemory(), ct);
                    _bufferPosition = 0;
                }

                if (_bufferLength == 0)
                {
                    return readAnyCharacter ? record.ToString() : null;
                }

                character = _buffer[_bufferPosition++];
            }

            if (_discardLeadingLineFeed)
            {
                _discardLeadingLineFeed = false;
                if (character == '\n')
                {
                    continue;
                }
            }

            readAnyCharacter = true;

            if (inQuotedField && quotePending)
            {
                if (character == '"')
                {
                    quotePending = false;
                    record.Append(character);
                    continue;
                }

                inQuotedField = false;
                quotePending = false;
                _unreadCharacter = character;
                continue;
            }

            if (character == '\r' && !inQuotedField)
            {
                _discardLeadingLineFeed = true;
                return record.ToString();
            }

            if (character == '\n' && !inQuotedField)
            {
                return record.ToString();
            }

            record.Append(character);

            if (inQuotedField)
            {
                quotePending = character == '"';
                continue;
            }

            if (character == '"' && atFieldStart)
            {
                inQuotedField = true;
                atFieldStart = false;
            }
            else if (character is ',' or '|')
            {
                atFieldStart = true;
            }
            else
            {
                atFieldStart = false;
            }
        }

    }

    private static List<string> ParseFields(string record, char delimiter)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotedField = false;
        var atFieldStart = true;
        var afterClosingQuote = false;

        for (var index = 0; index < record.Length; index++)
        {
            var character = record[index];

            if (inQuotedField)
            {
                if (character == '"')
                {
                    if (index + 1 < record.Length && record[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotedField = false;
                        afterClosingQuote = true;
                    }
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            if (afterClosingQuote)
            {
                if (character != delimiter)
                {
                    throw new XsvValidationException("A quoted field must be followed by a delimiter or the end of the record.");
                }

                fields.Add(field.ToString());
                field.Clear();
                atFieldStart = true;
                afterClosingQuote = false;
                continue;
            }

            if (character == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
                atFieldStart = true;
            }
            else if (character == '"' && atFieldStart)
            {
                inQuotedField = true;
                atFieldStart = false;
            }
            else
            {
                field.Append(character);
                atFieldStart = false;
            }
        }

        if (inQuotedField)
        {
            throw new XsvValidationException("A quoted field was not terminated before the end of the record.");
        }

        fields.Add(field.ToString());
        return fields;
    }
}

internal sealed record XsvParsedRecord(char Tag, IReadOnlyList<string> Fields);
