namespace WowStopwatcher.Core;

/// <summary>
/// Converts between UTC instants and local calendar days. Sessions belong to the local date of their start.
/// </summary>
public static class LocalDays
{
    /// <summary>The local calendar date of <paramref name="instant"/> in <paramref name="zone"/>.</summary>
    public static DateOnly ToLocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>
    /// The UTC instant at which local <paramref name="day"/> begins in <paramref name="zone"/>.
    /// Handles DST: a skipped midnight moves to the first valid local time, an ambiguous one uses its first occurrence.
    /// </summary>
    public static DateTimeOffset StartOfDayUtc(DateOnly day, TimeZoneInfo zone) =>
        LocalTimeUtc(day, TimeOnly.MinValue, zone);

    /// <summary>
    /// The UTC instant of local <paramref name="time"/> on <paramref name="day"/> in <paramref name="zone"/>.
    /// Handles DST: a skipped time moves to the first valid local time, an ambiguous one uses its first occurrence.
    /// </summary>
    public static DateTimeOffset LocalTimeUtc(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(15);
        }

        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>The half-open UTC range [start, end) covered by local <paramref name="day"/>.</summary>
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) DayRangeUtc(DateOnly day, TimeZoneInfo zone) =>
        (StartOfDayUtc(day, zone), StartOfDayUtc(day.AddDays(1), zone));
}
