namespace XsvHcdtHelper;

public enum OutputFormat { Csv, Parquet }
public enum FieldDelimiter { Auto, Pipe, Comma }

public sealed class XsvHcdtOptions
{
    public FieldDelimiter InputDelimiter { get; set; } = FieldDelimiter.Auto;
    public OutputFormat OutputFormat { get; set; } = OutputFormat.Csv;

    public bool ValidateTrailerCount { get; set; } = true;
    public bool ValidateHeaderTrailerMatch { get; set; } = true;
    public bool ValidateEnvelopeOrder { get; set; } = true;
    public bool StrictFieldCount { get; set; } = false;

    public int RowGroupSize { get; set; } = 50_000;
    public int BufferSize { get; set; } = 64 * 1024;
}