namespace XsvHcdtHelper;

public sealed class XsvValidationException : Exception
{
    public XsvValidationException(string message) : base(message)
    {
    }

    public XsvValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}