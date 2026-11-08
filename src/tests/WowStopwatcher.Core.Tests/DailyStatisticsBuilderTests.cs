namespace WowStopwatcher.Core.Tests;

public class DailyStatisticsBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 12, 20, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;

    private static StopwatchDefinition Def(string id, int order, int? goalMinutes = null, bool deleted = false, DateTimeOffset? activeSince = null) =>
        new(id, id.ToUpperInvariant(), "#" + id.PadRight(6, '0'),
            goalMinutes is { } g ? TimeSpan.FromMinutes(g) : null, null, order, deleted, ActiveSinceUtc: activeSince);

    private static Session Sess(int id, string sw, DateTimeOffset start, int minutes) =>
        new(id, sw, start, start.AddMinutes(minutes));

    [Fact]
    public void NoSessions_UsesMinimumScale()
    {
        var result = DailyStatisticsBuilder.Build([], [Def("a", 0)], Now, Zone);
        Assert.Empty(result.Days);
        Assert.Equal(540, result.ScaleMinutes);
        Assert.Equal(["0h", "3h", "6h", "9h"], result.Ticks.Select(t => t.Label));
        Assert.Equal([0.0, 1 / 3.0, 2 / 3.0, 1.0], result.Ticks.Select(t => t.Position));
    }

    [Fact]
    public void Scale_GrowsInThreeHourSteps()
    {
        var sessions = new[] { Sess(1, "a", new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero), 10 * 60 + 1) };
        var result = DailyStatisticsBuilder.Build(sessions, [Def("a", 0)], Now, Zone);
        Assert.Equal(720, result.ScaleMinutes);
        Assert.Equal(["0h", "4h", "8h", "12h"], result.Ticks.Select(t => t.Label));
    }

    [Fact]
    public void Days_AreNewestFirst_AndGroupedByLocalStartDate()
    {
        var plus3 = TimeZoneInfo.CreateCustomTimeZone("p3", TimeSpan.FromHours(3), "p3", "p3");
        var sessions = new[]
        {
            Sess(1, "a", new DateTimeOffset(2026, 3, 10, 22, 30, 0, TimeSpan.Zero), 60), // local 11th 01:30
            Sess(2, "a", new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero), 30),  // local 10th
        };
        var result = DailyStatisticsBuilder.Build(sessions, [Def("a", 0)], Now, plus3);
        Assert.Equal([new DateOnly(2026, 3, 11), new DateOnly(2026, 3, 10)], result.Days.Select(d => d.Date));
    }

    [Fact]
    public void Totals_ContainTimedAndGoalStopwatches_OrderedBySortOrder()
    {
        var defs = new[]
        {
            Def("c", 2),
            Def("a", 0, goalMinutes: 60),
            Def("b", 1, goalMinutes: 30),
            Def("d", 3, goalMinutes: 30, deleted: true),
            Def("e", 4),
        };
        var day = new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero);
        var sessions = new[]
        {
            Sess(1, "c", day, 30),
            Sess(2, "a", day.AddHours(1), 60),
            Sess(3, "d", day.AddHours(3), 30),
        };

        var stats = DailyStatisticsBuilder.Build(sessions, defs, Now, Zone).Days.Single();

        Assert.Equal(["a", "b", "c", "d"], stats.Totals.Select(t => t.StopwatchId));
        Assert.Equal(3, stats.SessionCount);
        Assert.Equal(TimeSpan.FromMinutes(120), stats.Total);
        Assert.Equal(0.5, stats.Totals[0].Share, 6);
        Assert.Equal(0, stats.Totals[1].Share);
        Assert.True(stats.Totals[0].GoalReached);
        Assert.False(stats.Totals[1].GoalReached);
        Assert.False(stats.Totals[3].GoalReached); // deleted
        Assert.Equal(1, stats.GoalsMet);
        Assert.Equal(2, stats.GoalCount);
        Assert.Equal(["a", "c", "d"], stats.Segments.Select(s => s.StopwatchId).Order(StringComparer.Ordinal).Take(3));
        Assert.Equal(60.0 / 540, stats.Segments.First(s => s.StopwatchId == "a").WidthFraction, 6);
    }

    [Fact]
    public void OvertimeShare_IsTimePastGoalOverDayTotal()
    {
        var day = new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero);
        var sessions = new[] { Sess(1, "a", day, 186), Sess(2, "b", day.AddHours(4), 92) };
        var stats = DailyStatisticsBuilder.Build(sessions, [Def("a", 0, goalMinutes: 180), Def("b", 1)], Now, Zone).Days.Single();
        Assert.Equal(6.0 / 278, stats.Totals[0].OvertimeShare, 6);
        Assert.Equal(186.0 / 278, stats.Totals[0].Share, 6);
    }

    [Fact]
    public void OvertimeShare_IsZero_WhenGoalNotReached_NoGoal_OrDeleted()
    {
        var day = new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero);
        var defs = new[] { Def("a", 0, goalMinutes: 120), Def("b", 1), Def("c", 2, goalMinutes: 10, deleted: true) };
        var sessions = new[] { Sess(1, "a", day, 60), Sess(2, "b", day.AddHours(2), 60), Sess(3, "c", day.AddHours(4), 60) };
        var totals = DailyStatisticsBuilder.Build(sessions, defs, Now, Zone).Days.Single().Totals;
        Assert.All(totals, t => Assert.Equal(0, t.OvertimeShare));
    }

    [Fact]
    public void Segments_AreChronological()
    {
        var day = new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero);
        var sessions = new[] { Sess(2, "b", day.AddHours(2), 10), Sess(1, "a", day, 10) };
        var stats = DailyStatisticsBuilder.Build(sessions, [Def("a", 0), Def("b", 1)], Now, Zone).Days.Single();
        Assert.Equal(["a", "b"], stats.Segments.Select(s => s.StopwatchId));
    }

    [Fact]
    public void GoalStopwatch_CreatedLater_NotListedOrCountedOnEarlierDays()
    {
        var d10 = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var d12 = new DateTimeOffset(2026, 3, 12, 9, 0, 0, TimeSpan.Zero);
        var defs = new[] { Def("a", 0), Def("g", 1, goalMinutes: 60, activeSince: new DateTimeOffset(2026, 3, 12, 8, 0, 0, TimeSpan.Zero)) };
        var sessions = new[] { Sess(1, "a", d10, 30), Sess(2, "a", d12, 30) };
        var days = DailyStatisticsBuilder.Build(sessions, defs, Now, Zone).Days;
        var early = days.Single(d => d.Date == new DateOnly(2026, 3, 10));
        var late = days.Single(d => d.Date == new DateOnly(2026, 3, 12));
        Assert.DoesNotContain(early.Totals, t => t.StopwatchId == "g");
        Assert.Equal(0, early.GoalCount);
        Assert.Contains(late.Totals, t => t.StopwatchId == "g");
        Assert.Equal(1, late.GoalCount);
    }

    [Fact]
    public void GoalStopwatch_RestoredAfterFirstSession_ListedFromFirstSession()
    {
        var d9 = new DateTimeOffset(2026, 3, 9, 9, 0, 0, TimeSpan.Zero);
        var d10 = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var d11 = new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero);
        var defs = new[] { Def("a", 0), Def("g", 1, goalMinutes: 60, activeSince: new DateTimeOffset(2026, 3, 12, 8, 0, 0, TimeSpan.Zero)) };
        var sessions = new[] { Sess(1, "g", d10, 10), Sess(2, "a", d9, 30), Sess(3, "a", d11, 30) };
        var days = DailyStatisticsBuilder.Build(sessions, defs, Now, Zone).Days;
        var before = days.Single(d => d.Date == new DateOnly(2026, 3, 9));
        var after = days.Single(d => d.Date == new DateOnly(2026, 3, 11));
        Assert.DoesNotContain(before.Totals, t => t.StopwatchId == "g");
        Assert.Equal(0, before.GoalCount);
        Assert.Contains(after.Totals, t => t.StopwatchId == "g");
        Assert.Equal(1, after.GoalCount);
    }

    [Fact]
    public void GoalStopwatch_NoSessionsAndNoActiveSince_ListedOnEveryDay()
    {
        var day = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var defs = new[] { Def("a", 0), Def("g", 1, goalMinutes: 60) };
        var stats = DailyStatisticsBuilder.Build([Sess(1, "a", day, 30)], defs, Now, Zone).Days.Single();
        Assert.Contains(stats.Totals, t => t.StopwatchId == "g");
        Assert.Equal(1, stats.GoalCount);
    }
}
