namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// A day of an invoice as shown: its lines and how they compare with the tracked time.
/// </summary>
/// <param name="Date">Local day.</param>
/// <param name="Edited">True when the lines are stored and edited; false when derived from sessions.</param>
/// <param name="Lines">The lines in order.</param>
/// <param name="TrackedMinutes">Minutes tracked that day by the billed stopwatches (sum of the live lines).</param>
/// <param name="HasRunningSession">True when a billed stopwatch is running and started that day.</param>
/// <param name="MovedOutMinutes">Minutes of this day that other days' lines hold (moved by merging across days); 0 when none.</param>
public sealed record InvoiceDay(
    DateOnly Date,
    bool Edited,
    IReadOnlyList<InvoiceLine> Lines,
    int TrackedMinutes,
    bool HasRunningSession,
    int MovedOutMinutes = 0)
{
    /// <summary>Sum of the line minutes.</summary>
    public int InvoicedMinutes => Lines.Sum(l => l.Minutes);

    /// <summary>Minutes of the lines with a note (the billed ones).</summary>
    public int BilledMinutes => Lines.Where(InvoiceBilling.IsBilled).Sum(l => l.Minutes);

    /// <summary>Minutes of the lines without a note; shown but not billed.</summary>
    public int UnallocatedMinutes => Lines.Where(l => !InvoiceBilling.IsBilled(l)).Sum(l => l.Minutes);

    /// <summary>
    /// Tracked minus the day's own minutes for an edited day or a day with time moved out; 0 for a live day. The own minutes are the
    /// non-custom line minutes without the time moved in from other days and with the time moved out to other days.
    /// Custom lines are not tracked time, so they never cause drift.
    /// </summary>
    public int DriftMinutes => Edited || MovedOutMinutes > 0 ? TrackedMinutes - OwnMinutes : 0;

    private int OwnMinutes => Lines.Where(l => !l.IsCustom).Sum(l => l.Minutes - InvoiceLineMoves.Total(l)) + MovedOutMinutes;
}
