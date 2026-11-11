namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>The result of <see cref="InvoicingService.BuildPreviewAsync"/>.</summary>
/// <param name="Png">The first page as PNG; null when it could not be made.</param>
/// <param name="Blocks">Reasons the preview was not started.</param>
/// <param name="PdfError">The conversion error code, when the conversion failed.</param>
/// <param name="Detail">Error detail text, when failed.</param>
public sealed record PreviewResult(
    byte[]? Png,
    IReadOnlyList<GenerateBlockReason>? Blocks = null,
    PdfErrorCode? PdfError = null,
    string? Detail = null);
