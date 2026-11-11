namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Totals of one invoice. Only allocated time (lines with a note) is billed.</summary>
/// <param name="BilledMinutes">Minutes of lines with a note.</param>
/// <param name="UnallocatedMinutes">Minutes of lines without a note; shown, not in the invoice.</param>
/// <param name="Amount">Amount for the billed minutes (at the frozen rate when sent).</param>
/// <param name="Rate">Hourly rate in effect (the frozen one when sent).</param>
/// <param name="Currency">Currency in effect (the frozen one when sent).</param>
public sealed record InvoiceSummary(int BilledMinutes, int UnallocatedMinutes, decimal Amount, decimal Rate, string Currency);
