namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Why a PDF conversion failed; the UI shows localized text for the code plus the detail.</summary>
public enum PdfErrorCode
{
    /// <summary>No converter is installed.</summary>
    NotAvailable,

    /// <summary>The converter took too long.</summary>
    Timeout,

    /// <summary>The converter failed or reported an error.</summary>
    Failed,

    /// <summary>The converter finished but produced no PDF.</summary>
    OutputMissing,
}
