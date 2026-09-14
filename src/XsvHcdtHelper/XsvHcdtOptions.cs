namespace XsvHcdtHelper;

public enum OutputFormat { Csv, Parquet }
public enum FieldDelimiter { Auto, Pipe, Comma }

/// <summary>
/// Controls how the double-quote character is interpreted in the input.
/// </summary>
public enum QuoteHandling
{
    /// <summary>
    /// RFC 4180 quoting. A field whose first character is <c>"</c> is treated as quoted:
    /// the delimiter and line breaks are literal until the closing quote, and <c>""</c>
    /// is an escaped quote.
    /// </summary>
    Rfc4180,

    /// <summary>
    /// No quoting. Every <c>"</c> is ordinary data and fields are split on the delimiter
    /// alone. Use this for legacy extracts that emit unquoted free-text columns, where a
    /// leading <c>"</c> would otherwise be misread as the start of a quoted field.
    /// </summary>
    None,
}

public sealed class XsvHcdtOptions
{
    public FieldDelimiter InputDelimiter { get; set; } = FieldDelimiter.Auto;

    /// <summary>
    /// How <c>"</c> is interpreted in the input. Defaults to <see cref="QuoteHandling.Rfc4180"/>.
    /// Set to <see cref="QuoteHandling.None"/> for feeds that never quote their fields.
    /// </summary>
    public QuoteHandling InputQuoting { get; set; } = QuoteHandling.Rfc4180;

    public OutputFormat OutputFormat { get; set; } = OutputFormat.Csv;

    public bool ValidateTrailerCount { get; set; } = true;
    public bool ValidateHeaderTrailerMatch { get; set; } = true;
    public bool ValidateEnvelopeOrder { get; set; } = true;
    public bool StrictFieldCount { get; set; } = false;

    /// <summary>
    /// Whether an <c>H</c> (Header) record must be the first record. Defaults to <c>true</c>.
    /// Set to <c>false</c> to accept input that starts straight at the <c>C</c> or <c>D</c>
    /// records, for example one slice of a file that was split after export.
    /// </summary>
    public bool RequireHeader { get; set; } = true;

    /// <summary>
    /// Whether a <c>T</c> (Trailer) record must close the input. Defaults to <c>true</c>.
    /// Set to <c>false</c> to accept input that simply ends after the last <c>D</c> record.
    /// When no trailer is present there is nothing to validate against, so
    /// <see cref="ValidateTrailerCount"/> and <see cref="ValidateHeaderTrailerMatch"/> are
    /// skipped and <see cref="XsvValidationReport.DeclaredRecordCount"/> stays 0.
    /// </summary>
    public bool RequireTrailer { get; set; } = true;

    public int RowGroupSize { get; set; } = 50_000;
    public int BufferSize { get; set; } = 64 * 1024;
    public Type? CustomSinkType { get; set; }
    public XsvHcdtOptions Clone() => (XsvHcdtOptions)MemberwiseClone();
}