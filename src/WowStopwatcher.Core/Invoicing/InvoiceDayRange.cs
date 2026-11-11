namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// The time an invoice day covers: from its start on its own date to the next day's start.
/// </summary>
/// <param name="Date">Local date the invoice day is named after.</param>
/// <param name="StartMinutes">Start of the day, minutes after local midnight of <paramref name="Date"/>.</param>
/// <param name="EndMinutes">End of the day, minutes after local midnight of the next date (the next day's start).</param>
/// <param name="StartUtc">Start instant, inclusive.</param>
/// <param name="EndUtc">End instant, exclusive.</param>
/// <param name="IsOverridden">True when the start of this day was set for it alone.</param>
public sealed record InvoiceDayRange(
    DateOnly Date,
    int StartMinutes,
    int EndMinutes,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool IsOverridden)
{
    /// <summary>True when the day is the plain calendar day (midnight to midnight).</summary>
    public bool IsCalendarDay => StartMinutes == 0 && EndMinutes == 0;
}
