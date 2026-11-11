namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Totals of one invoice. Only allocated time (lines with a note) is billed, unless reported time replaces it.</summary>
/// <param name="BilledMinutes">Minutes of lines with a note.</param>
/// <param name="UnallocatedMinutes">Minutes of lines without a note; shown, not in the invoice.</param>
/// <param name="TrackedMinutes">Minutes tracked by the billed stopwatches in the month (sum of the days' tracked time).</param>
/// <param name="InvoicedMinutes">Minutes the invoice bills: the reported time when set, otherwise the billed minutes (the frozen value when sent).</param>
/// <param name="ReportedMinutes">Reported time for the month; null when not set.</param>
/// <param name="Amount">Amount for the invoiced minutes (at the frozen rate when sent).</param>
/// <param name="Rate">Hourly rate in effect (the frozen one when sent).</param>
/// <param name="Currency">Currency in effect (the frozen one when sent).</param>
public sealed record InvoiceSummary(
    int BilledMinutes,
    int UnallocatedMinutes,
    int TrackedMinutes,
    int InvoicedMinutes,
    int? ReportedMinutes,
    decimal Amount,
    decimal Rate,
    string Currency);
