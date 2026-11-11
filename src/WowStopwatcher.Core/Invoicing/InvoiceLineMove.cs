namespace WowStopwatcher.Core.Invoicing;

/// <summary>Time that was moved into a line from another day, so that day's move is not counted as drift.</summary>
/// <param name="FromDate">The day the time was moved from.</param>
/// <param name="Minutes">Moved minutes, more than 0.</param>
public sealed record InvoiceLineMove(DateOnly FromDate, int Minutes);
