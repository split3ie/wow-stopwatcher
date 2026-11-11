namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>The result of <see cref="InvoicingService.GenerateAsync"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="FilePath">The written file, when generated.</param>
/// <param name="InvoiceNumber">The invoice number, when generated.</param>
/// <param name="Blocks">Reasons, when blocked.</param>
/// <param name="PdfError">The conversion error code, when the conversion failed.</param>
/// <param name="Detail">Error detail text, when failed.</param>
public sealed record GenerateResult(
    GenerateOutcome Outcome,
    string? FilePath = null,
    int? InvoiceNumber = null,
    IReadOnlyList<GenerateBlockReason>? Blocks = null,
    PdfErrorCode? PdfError = null,
    string? Detail = null);
