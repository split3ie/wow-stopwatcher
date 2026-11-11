using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class InvoiceDayRangesTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly InvoicePeriod November = new(2026, 11);
    private static readonly DateTimeOffset Now = new(2026, 12, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Dictionary<string, string> Names = new() { ["a"] = "Alpha" };
    private static readonly HashSet<string> Assigned = ["a"];

    private static DateTimeOffset At(int month, int day, int hour, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    private static Session S(DateTimeOffset start, DateTimeOffset? stop) => new(1, "a", start, stop);

    private static IReadOnlyList<InvoiceDay> Build(int defaultStart, Dictionary<DateOnly, int>? overrides, params Session[] sessions) =>
        InvoiceLineBuilder.Build(sessions, Names, Assigned, November, Utc, Now, defaultStart, overrides);

    [Fact]
    public void DefaultZero_GivesCalendarDays()
    {
        var ranges = InvoiceDayRanges.ForPeriod(November, 0, null, Utc);

        Assert.Equal(30, ranges.Count);
        Assert.All(ranges, r => Assert.True(r.IsCalendarDay));
        Assert.Equal(At(11, 3, 0), ranges[2].StartUtc);
        Assert.Equal(At(11, 4, 0), ranges[2].EndUtc);
    }

    [Fact]
    public void DefaultStart_DayRunsToTheSameTimeNextDay()
    {
        var range = InvoiceDayRanges.For(new DateOnly(2026, 11, 3), 20 * 60, null, Utc);

        Assert.Equal(At(11, 3, 20), range.StartUtc);
        Assert.Equal(At(11, 4, 20), range.EndUtc);
        Assert.False(range.IsCalendarDay);
        Assert.False(range.IsOverridden);
    }

    [Fact]
    public void Override_ShiftsStartOfThatDayAndEndOfThePreviousDay()
    {
        var overrides = new Dictionary<DateOnly, int> { [new DateOnly(2026, 11, 4)] = 18 * 60 };

        var third = InvoiceDayRanges.For(new DateOnly(2026, 11, 3), 20 * 60, overrides, Utc);
        var fourth = InvoiceDayRanges.For(new DateOnly(2026, 11, 4), 20 * 60, overrides, Utc);

        Assert.Equal(At(11, 4, 18), third.EndUtc);
        Assert.Equal(At(11, 4, 18), fourth.StartUtc);
        Assert.True(fourth.IsOverridden);
        Assert.Equal(18 * 60, third.EndMinutes);
    }

    [Fact]
    public void RangesNeverOverlap()
    {
        var overrides = new Dictionary<DateOnly, int> { [new DateOnly(2026, 11, 10)] = 23 * 60 + 59, [new DateOnly(2026, 11, 11)] = 0 };

        var ranges = InvoiceDayRanges.ForPeriod(November, 20 * 60, overrides, Utc);

        for (var i = 1; i < ranges.Count; i++)
        {
            Assert.Equal(ranges[i - 1].EndUtc, ranges[i].StartUtc);
            Assert.True(ranges[i].EndUtc >= ranges[i].StartUtc);
        }
    }

    [Fact]
    public void Dst_DaysFollowLocalTime()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "W. Europe Standard Time" : "Europe/Berlin");

        // 2026-10-25: clocks go back, so the midnight to midnight day is 25 hours long; a 20:00 day starts after the change.
        var plain = InvoiceDayRanges.For(new DateOnly(2026, 10, 25), 0, null, zone);
        var evening = InvoiceDayRanges.For(new DateOnly(2026, 10, 25), 20 * 60, null, zone);
        Assert.Equal(TimeSpan.FromHours(25), plain.EndUtc - plain.StartUtc);
        Assert.Equal(TimeSpan.FromHours(24), evening.EndUtc - evening.StartUtc);

        // 2026-03-29: 02:30 does not exist and moves to 03:00 (01:00 UTC).
        var skipped = InvoiceDayRanges.For(new DateOnly(2026, 3, 29), 2 * 60 + 30, null, zone);
        Assert.Equal(At(3, 29, 1), skipped.StartUtc);
    }

    [Fact]
    public void ShiftSplitAtMidnight_JoinsIntoOneDay()
    {
        // 20:00 to 04:00, stored as two sessions split at midnight.
        var days = Build(
            20 * 60,
            null,
            S(At(11, 3, 20), At(11, 4, 0)),
            S(At(11, 4, 0), At(11, 4, 4)));

        var day = Assert.Single(days);
        Assert.Equal(new DateOnly(2026, 11, 3), day.Date);
        Assert.Equal(480, day.TrackedMinutes);
        Assert.Equal(480, Assert.Single(day.Lines).Minutes);
    }

    [Fact]
    public void SessionAcrossTheBoundary_IsClippedIntoTwoDays()
    {
        var days = Build(20 * 60, null, S(At(11, 4, 19), At(11, 4, 22)));

        Assert.Equal([new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 4)], days.Select(d => d.Date));
        Assert.Equal([60, 120], days.Select(d => d.TrackedMinutes));
    }

    [Fact]
    public void OverrideShiftsTheEndOfThePreviousDay()
    {
        var overrides = new Dictionary<DateOnly, int> { [new DateOnly(2026, 11, 4)] = 2 * 60 };

        // Shift 20:00-04:00; the next day starts at 02:00 instead of 20:00.
        var days = Build(20 * 60, overrides, S(At(11, 3, 20), At(11, 4, 0)), S(At(11, 4, 0), At(11, 4, 4)));

        Assert.Equal([new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 4)], days.Select(d => d.Date));
        Assert.Equal([360, 120], days.Select(d => d.TrackedMinutes));
    }

    [Fact]
    public void FirstOfMonth_TimeBeforeTheStartBelongsToThePreviousMonth()
    {
        var shift = new[] { S(At(10, 31, 20), At(11, 1, 0)), S(At(11, 1, 0), At(11, 1, 4)) };

        var november = Build(20 * 60, null, shift);
        var october = InvoiceLineBuilder.Build(shift, Names, Assigned, new InvoicePeriod(2026, 10), Utc, Now, 20 * 60);

        Assert.Empty(november);
        var day = Assert.Single(october);
        Assert.Equal(new DateOnly(2026, 10, 31), day.Date);
        Assert.Equal(480, day.TrackedMinutes);
    }

    [Fact]
    public void OpenSession_CountsUpToNowAndMarksTheRunningDay()
    {
        var now = At(11, 4, 1);
        var days = InvoiceLineBuilder.Build([S(At(11, 4, 0), null)], Names, Assigned, November, Utc, now, 20 * 60);

        var day = Assert.Single(days);
        Assert.Equal(new DateOnly(2026, 11, 3), day.Date);
        Assert.Equal(60, day.TrackedMinutes);
        Assert.True(day.HasRunningSession);
    }
}
