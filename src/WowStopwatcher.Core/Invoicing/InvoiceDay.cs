namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// A day of an invoice as shown: its lines and how they compare with the tracked time.
/// </summary>
/// <param name="Date">Local day.</param>
/// <param name="Edited">True when the lines are stored and edited; false when derived from sessions.</param>
/// <param name="Lines">The lines in order.</param>
/// <param name="TrackedMinutes">Minutes tracked that day by the billed stopwatches (sum of the live lines).</param>
/// <param name="HasRunningSession">True when a billed stopwatch is running and started that day.</param>
public sealed record InvoiceDay(
    DateOnly Date,
    bool Edited,
    IReadOnlyList<InvoiceLine> Lines,
    int TrackedMinutes,
    bool HasRunningSession)
{
    /// <summary>Sum of the line minutes.</summary>
    public int InvoicedMinutes => Lines.Sum(l => l.Minutes);

    /// <summary>Minutes of the lines with a note (the billed ones).</summary>
    public int BilledMinutes => Lines.Where(InvoiceBilling.IsBilled).Sum(l => l.Minutes);

    /// <summary>Minutes of the lines without a note; shown but not billed.</summary>
    public int UnallocatedMinutes => Lines.Where(l => !InvoiceBilling.IsBilled(l)).Sum(l => l.Minutes);

    /// <summary>Tracked minus invoiced minutes for an edited day; 0 for a live day.</summary>
    public int DriftMinutes => Edited ? TrackedMinutes - InvoicedMinutes : 0;
}
