namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>The values frozen when an invoice is marked as sent (stored as JSON).</summary>
/// <param name="Rate">Hourly rate.</param>
/// <param name="Currency">Currency code.</param>
/// <param name="Minutes">Total invoiced minutes.</param>
/// <param name="Amount">Total amount.</param>
/// <param name="Values">Resolved placeholder values by alias (errors are left out).</param>
/// <param name="FileName">Name of the generated file, if any.</param>
/// <param name="TemplateSha256">Hash of the template used.</param>
public sealed record InvoiceSnapshot(
    decimal Rate,
    string Currency,
    int Minutes,
    decimal Amount,
    Dictionary<string, string> Values,
    string? FileName,
    string? TemplateSha256);
