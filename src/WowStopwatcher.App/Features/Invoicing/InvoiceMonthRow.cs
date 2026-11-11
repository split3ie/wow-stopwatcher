using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>One month in a company's invoices list.</summary>
/// <param name="Period">The month.</param>
/// <param name="Invoice">The invoice row; null until the first edit, generation or mark as sent.</param>
/// <param name="InvoicedMinutes">Minutes the invoice bills: the reported time when set, otherwise the lines with a note (the frozen value when sent).</param>
/// <param name="UnallocatedMinutes">Minutes of lines without a note: not in the invoice.</param>
/// <param name="TrackedMinutes">Minutes tracked by the billed stopwatches in the month.</param>
/// <param name="ReportedMinutes">Reported time for the month; null when not set.</param>
/// <param name="Amount">Amount for the invoiced minutes (at the frozen rate when sent).</param>
/// <param name="Currency">Currency of the amount (the frozen one when sent, the company's otherwise).</param>
/// <param name="Status">Draft, pending or sent.</param>
/// <param name="FileExists">True when the generated file is still on disk.</param>
/// <param name="IsCurrentMonth">True for the current local month.</param>
public sealed record InvoiceMonthRow(
    InvoicePeriod Period,
    Invoice? Invoice,
    int InvoicedMinutes,
    int UnallocatedMinutes,
    int TrackedMinutes,
    int? ReportedMinutes,
    decimal Amount,
    string Currency,
    InvoiceMonthStatus Status,
    bool FileExists,
    bool IsCurrentMonth);
