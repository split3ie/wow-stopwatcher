namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>A DOCX to PDF conversion failed or no converter is available. <see cref="Code"/> says why; the message is the detail.</summary>
public sealed class PdfConversionException : Exception
{
    /// <summary>Creates the exception with a code, a detail message and optionally the cause.</summary>
    public PdfConversionException(PdfErrorCode code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>Why the conversion failed.</summary>
    public PdfErrorCode Code { get; }
}
