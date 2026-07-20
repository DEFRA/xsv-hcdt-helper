namespace XsvHcdtHelper;

public sealed class XsvValidationException : Exception
{
    public XsvValidationException(string message) : base(message)
    {
    }

    public XsvValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>The expected value for the failed validation rule, when applicable.</summary>
    public string? Expected { get; init; }

    /// <summary>The actual value observed, when applicable.</summary>
    public string? Actual { get; init; }
}