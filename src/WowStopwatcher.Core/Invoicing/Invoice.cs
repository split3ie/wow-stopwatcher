namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// The invoice of one company for one month. A row exists only after the first edit, generation or mark as sent.
/// </summary>
/// <param name="Id">Database id.</param>
/// <param name="CompanyId">Billed company.</param>
/// <param name="Period">Calendar month.</param>
/// <param name="Status">Pending or sent.</param>
/// <param name="Number">Invoice number, assigned on the first generation and kept afterwards.</param>
/// <param name="FilePath">Path of the last generated PDF.</param>
/// <param name="GeneratedUtc">When the PDF was last generated.</param>
/// <param name="GeneratedInputHash">See <see cref="InvoiceInputHash"/>; taken at the last generation.</param>
/// <param name="SentUtc">When it was marked as sent.</param>
/// <param name="SnapshotJson">Values frozen when it was marked as sent.</param>
/// <param name="ReportedMinutes">Total minutes reported for the month; replaces the sum of the billed lines. Null: the sum of the billed lines is used.</param>
public sealed record Invoice(
    long Id,
    string CompanyId,
    InvoicePeriod Period,
    InvoiceStatus Status,
    int? Number,
    string? FilePath,
    DateTimeOffset? GeneratedUtc,
    string? GeneratedInputHash,
    DateTimeOffset? SentUtc,
    string? SnapshotJson,
    int? ReportedMinutes = null);
