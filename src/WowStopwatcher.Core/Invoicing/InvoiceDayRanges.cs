namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// Computes the ranges of invoice days. Invoice day D runs from start(D) to start(D+1), where start(X) is local date X at the
/// day's own start (an override, else the company default). Each start lies on its own date, so ranges never overlap.
/// </summary>
public static class InvoiceDayRanges
{
    /// <summary>The range of one invoice day.</summary>
    /// <param name="date">The invoice day.</param>
    /// <param name="defaultStartMinutes">The company's default start, minutes after midnight.</param>
    /// <param name="overrides">Starts set for single dates; may be null.</param>
    /// <param name="zone">Zone that turns local times into instants.</param>
    public static InvoiceDayRange For(
        DateOnly date,
        int defaultStartMinutes,
        IReadOnlyDictionary<DateOnly, int>? overrides,
        TimeZoneInfo zone)
    {
        var overridden = overrides is not null && overrides.ContainsKey(date);
        var start = StartMinutes(date, defaultStartMinutes, overrides);
        var end = StartMinutes(date.AddDays(1), defaultStartMinutes, overrides);
        var startUtc = StartUtc(date, start, zone);
        var endUtc = StartUtc(date.AddDays(1), end, zone);
        return new InvoiceDayRange(date, start, end, startUtc, endUtc < startUtc ? startUtc : endUtc, overridden);
    }

    /// <summary>The ranges of every day of <paramref name="period"/>, in date order.</summary>
    public static IReadOnlyList<InvoiceDayRange> ForPeriod(
        InvoicePeriod period,
        int defaultStartMinutes,
        IReadOnlyDictionary<DateOnly, int>? overrides,
        TimeZoneInfo zone) =>
        Enumerable.Range(0, period.LastDay.Day)
            .Select(i => For(period.FirstDay.AddDays(i), defaultStartMinutes, overrides, zone))
            .ToList();

    /// <summary>The UTC instant [first start, end of last day) the ranges of <paramref name="period"/> cover.</summary>
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) WindowUtc(IReadOnlyList<InvoiceDayRange> ranges) =>
        (ranges[0].StartUtc, ranges[^1].EndUtc);

    private static int StartMinutes(DateOnly date, int defaultStartMinutes, IReadOnlyDictionary<DateOnly, int>? overrides) =>
        overrides is not null && overrides.TryGetValue(date, out var value) ? value : defaultStartMinutes;

    private static DateTimeOffset StartUtc(DateOnly date, int minutes, TimeZoneInfo zone) =>
        LocalDays.LocalTimeUtc(date, TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes)), zone);
}
