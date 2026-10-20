namespace PdfConverter.Api;

/// <summary>An expected conversion failure that maps to an HTTP response.</summary>
public sealed class ConversionException : Exception
{
    public ConversionException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }

    public string Code { get; }
}
