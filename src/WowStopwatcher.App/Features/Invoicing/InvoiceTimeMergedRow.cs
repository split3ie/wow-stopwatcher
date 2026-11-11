namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>A read-only invoice line that merges this stopwatch with others.</summary>
/// <param name="TimeText">Line time as h:mm.</param>
/// <param name="Note">Line note; may be empty.</param>
/// <param name="Sources">Names of the merged stopwatches.</param>
public sealed record InvoiceTimeMergedRow(string TimeText, string Note, string Sources);
