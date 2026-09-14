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

    public int RowGroupSize { get; set; } = 50_000;
    public int BufferSize { get; set; } = 64 * 1024;
    public Type? CustomSinkType { get; set; }
    public XsvHcdtOptions Clone() => (XsvHcdtOptions)MemberwiseClone();
}