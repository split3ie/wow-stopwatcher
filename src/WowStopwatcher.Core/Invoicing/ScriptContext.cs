namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// The values a script can read.
/// </summary>
/// <param name="Hours">Total hours.</param>
/// <param name="Minutes">Total minutes.</param>
/// <param name="Rate">Hourly rate.</param>
/// <param name="Amount">Total amount.</param>
/// <param name="Currency">Currency code.</param>
/// <param name="PeriodMonth">Period as "yyyy-MM".</param>
/// <param name="PeriodStart">First day of the period.</param>
/// <param name="PeriodEnd">Last day of the period.</param>
/// <param name="PeriodName">Period name such as "November 2026".</param>
/// <param name="InvoiceNumber">Invoice number; null when not assigned yet.</param>
/// <param name="Lines">The invoice lines.</param>
/// <param name="Placeholder">Resolves another placeholder by alias; null when unknown or failed.</param>
public sealed record ScriptContext(
    decimal Hours,
    int Minutes,
    decimal Rate,
    decimal Amount,
    string Currency,
    string PeriodMonth,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string PeriodName,
    int? InvoiceNumber,
    IReadOnlyList<InvoiceLine> Lines,
    Func<string, string?> Placeholder);
