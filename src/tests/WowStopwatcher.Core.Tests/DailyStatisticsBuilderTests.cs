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
    public void Scale_FitsCombinedBar_WhereOverlapCountsPerStopwatch()
    {
        var day = new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero);
        // Wall clock 6h (below the 9h minimum), but the combined bar is 6h + 5h = 11h long.
        var sessions = new[] { Sess(1, "a", day, 6 * 60), Sess(2, "b", day.AddHours(1), 5 * 60) };
        var result = DailyStatisticsBuilder.Build(sessions, [Def("a", 0), Def("b", 1)], Now, Zone);

        Assert.Equal(720, result.ScaleMinutes);
        Assert.Equal(TimeSpan.FromHours(6), result.Days.Single().Total);
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
        Assert.True(stats.Totals[3].GoalReached); // deleted, but it has time that day
        Assert.Equal(2, stats.GoalsMet);
        Assert.Equal(3, stats.GoalCount);
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
    public void OvertimeShare_IsZero_WhenGoalNotReached_OrNoGoal_AndCountedForDeletedGoal()
    {
        var day = new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero);
        var defs = new[] { Def("a", 0, goalMinutes: 120), Def("b", 1), Def("c", 2, goalMinutes: 10, deleted: true) };
        var sessions = new[] { Sess(1, "a", day, 60), Sess(2, "b", day.AddHours(2), 60), Sess(3, "c", day.AddHours(4), 60) };
        var totals = DailyStatisticsBuilder.Build(sessions, defs, Now, Zone).Days.Single().Totals;
        Assert.Equal(0, totals[0].OvertimeShare);
        Assert.Equal(0, totals[1].OvertimeShare);
        Assert.Equal(50.0 / 180, totals[2].OvertimeShare, 6);
    }

    [Fact]
    public void DeletedGoalStopwatch_CountsOnlyOnDaysWithTime()
    {
        var d10 = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var d11 = new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero);
        var defs = new[] { Def("a", 0), Def("g", 1, goalMinutes: 60, deleted: true) };
        var sessions = new[] { Sess(1, "g", d10, 30), Sess(2, "a", d11, 30) };
        var days = DailyStatisticsBuilder.Build(sessions, defs, Now, Zone).Days;
        var withTime = days.Single(d => d.Date == new DateOnly(2026, 3, 10));
        var without = days.Single(d => d.Date == new DateOnly(2026, 3, 11));
        Assert.Equal(TimeSpan.FromMinutes(60), withTime.Totals.Single(t => t.StopwatchId == "g").Goal);
        Assert.Equal(1, withTime.GoalCount);
        Assert.Equal(0, withTime.GoalsMet);
        Assert.DoesNotContain(without.Totals, t => t.StopwatchId == "g");
        Assert.Equal(0, without.GoalCount);
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

    private static readonly DateTimeOffset Day1 = new(2026, 3, 9, 9, 0, 0, TimeSpan.Zero);

    private static GoalChange Change(string sw, int day, int? previousMinutes) =>
        new(sw, new DateTimeOffset(2026, 3, day, 12, 0, 0, TimeSpan.Zero), previousMinutes is { } m ? TimeSpan.FromMinutes(m) : null);

    private static List<DayStatistics> GoalDays(IReadOnlyList<GoalChange> changes, StopwatchDefinition def) =>
        DailyStatisticsBuilder.Build(
            [Sess(1, "a", Day1, 400), Sess(2, "a", Day1.AddDays(1), 400), Sess(3, "a", Day1.AddDays(2), 400)],
            [def], Now, Zone, changes).Days.OrderBy(d => d.Date).ToList();

    [Fact]
    public void GoalChange_EarlierDaysUseGoalInEffectThen()
    {
        // 6h -> 8h changed on March 10: March 9 used 6h, March 10 and later use 8h.
        var days = GoalDays([Change("a", 10, 360)], Def("a", 0, goalMinutes: 480));
        Assert.Equal(TimeSpan.FromHours(6), days[0].Totals.Single().Goal);
        Assert.True(days[0].Totals.Single().GoalReached);
        Assert.Equal(1, days[0].GoalsMet);
        Assert.Equal(TimeSpan.FromHours(8), days[1].Totals.Single().Goal);
        Assert.False(days[1].Totals.Single().GoalReached);
        Assert.Equal(0, days[1].GoalsMet);
        Assert.Equal(TimeSpan.FromHours(8), days[2].Totals.Single().Goal);
    }

    [Fact]
    public void NoGoalChanges_UsesCurrentGoal()
    {
        var days = GoalDays([], Def("a", 0, goalMinutes: 480));
        Assert.All(days, d => Assert.Equal(TimeSpan.FromHours(8), d.Totals.Single().Goal));
    }

    [Fact]
    public void GoalAddedLater_EarlierDaysHaveNoGoal()
    {
        var days = GoalDays([Change("a", 10, null)], Def("a", 0, goalMinutes: 480));
        Assert.Null(days[0].Totals.Single().Goal);
        Assert.Equal(0, days[0].GoalCount);
        Assert.Equal(0, days[0].GoalsMet);
        Assert.Equal(TimeSpan.FromHours(8), days[1].Totals.Single().Goal);
        Assert.Equal(1, days[1].GoalCount);
    }

    [Fact]
    public void GoalRemovedLater_EarlierDaysKeepOldGoal()
    {
        var days = GoalDays([Change("a", 11, 360)], Def("a", 0));
        Assert.Equal(TimeSpan.FromHours(6), days[0].Totals.Single().Goal);
        Assert.Equal(TimeSpan.FromHours(6), days[1].Totals.Single().Goal);
        Assert.Null(days[2].Totals.Single().Goal);
        Assert.Equal(0, days[2].GoalCount);
    }

    [Fact]
    public void TwoGoalChanges_EachDayGetsItsOwnGoal()
    {
        // 4h -> 6h on March 10, 6h -> 8h on March 11.
        var days = GoalDays([Change("a", 10, 240), Change("a", 11, 360)], Def("a", 0, goalMinutes: 480));
        Assert.Equal(TimeSpan.FromHours(4), days[0].Totals.Single().Goal);
        Assert.Equal(TimeSpan.FromHours(6), days[1].Totals.Single().Goal);
        Assert.Equal(TimeSpan.FromHours(8), days[2].Totals.Single().Goal);
    }

    private static StopwatchDefinition CycleDef(string id, int goalHours, DateTimeOffset anchor, bool deleted = false) =>
        new(id, id.ToUpperInvariant(), "#" + id.PadRight(6, '0'), TimeSpan.FromHours(goalHours), null, 0, deleted,
            GoalSinceUtc: anchor, GoalPerCycle: true);

    private static Session Span(int id, string sw, DateTimeOffset start, DateTimeOffset stop) => new(id, sw, start, stop);

    private static DateTimeOffset At(int day, int hour) => new(2026, 3, day, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PerCycle_GoalMarkGoesOnCycleStartDate()
    {
        var def = CycleDef("a", 8, At(9, 0));
        var sessions = new[]
        {
            Span(1, "a", At(9, 20), At(10, 0)),
            Span(2, "a", At(10, 0), At(10, 4)),
            Span(3, "a", At(10, 4), At(10, 6)),
        };

        var days = DailyStatisticsBuilder.Build(sessions, [def], Now, Zone).Days;
        var tue = days[0];
        var mon = days[1];

        var m = mon.Totals.Single();
        Assert.Equal(TimeSpan.FromHours(4), mon.Total);
        Assert.Equal(TimeSpan.FromHours(8), m.Goal);
        Assert.True(m.GoalReached);
        Assert.Equal(1, m.GoalsReachedCount);
        Assert.Equal(At(10, 4), m.GoalReachedUtc);
        Assert.Equal(0, m.OvertimeShare);
        Assert.Null(m.CycleProgress);
        Assert.Equal(1, mon.GoalsMet);
        Assert.Equal(1, mon.GoalCount);

        var t = tue.Totals.Single();
        Assert.Equal(TimeSpan.FromHours(6), tue.Total);
        Assert.Equal(TimeSpan.FromHours(8), t.Goal);
        Assert.False(t.GoalReached);
        Assert.Equal(TimeSpan.FromHours(2), t.CycleProgress);
        Assert.Null(t.GoalReachedUtc);
        Assert.Equal(0, t.OvertimeShare);
        Assert.Equal(0, tue.GoalsMet);
        Assert.Equal(1, tue.GoalCount);
    }

    [Fact]
    public void PerCycle_UnsplitSession_CountsOnStartDate()
    {
        var def = CycleDef("a", 8, At(9, 0));
        var sessions = new[] { Span(1, "a", At(9, 20), At(10, 4)), Span(2, "a", At(10, 4), At(10, 6)) };

        var days = DailyStatisticsBuilder.Build(sessions, [def], Now, Zone).Days;
        var tue = days[0];
        var mon = days[1];

        var m = mon.Totals.Single();
        Assert.Equal(TimeSpan.FromHours(8), mon.Total); // the whole session counts on its start date
        Assert.Equal(TimeSpan.FromHours(8), m.Goal);
        Assert.True(m.GoalReached);
        Assert.Equal(1, m.GoalsReachedCount);
        Assert.Equal(At(10, 4), m.GoalReachedUtc);
        Assert.Equal(0, m.OvertimeShare);
        Assert.Null(m.CycleProgress);
        Assert.Equal(1, mon.GoalsMet);
        Assert.Equal(1, mon.GoalCount);

        var t = tue.Totals.Single();
        Assert.Equal(TimeSpan.FromHours(2), tue.Total);
        Assert.Equal(TimeSpan.FromHours(8), t.Goal);
        Assert.False(t.GoalReached);
        Assert.Equal(TimeSpan.FromHours(2), t.CycleProgress);
        Assert.Null(t.GoalReachedUtc);
        Assert.Equal(0, t.OvertimeShare);
        Assert.Equal(0, tue.GoalsMet);
        Assert.Equal(1, tue.GoalCount);
    }

    [Fact]
    public void PerCycle_LongCycleSpanningDays_OnlyStartDateHasGoal()
    {
        var def = CycleDef("a", 73, At(1, 0));
        // 24h on the 1st, 24h on the 2nd, 24h on the 3rd, 1h on the 4th: 73h in total.
        var sessions = new[]
        {
            Span(1, "a", At(1, 0), At(2, 0)),
            Span(2, "a", At(2, 0), At(3, 0)),
            Span(3, "a", At(3, 0), At(4, 0)),
            Span(4, "a", At(4, 0), At(4, 1)),
        };

        var days = DailyStatisticsBuilder.Build(sessions, [def], Now, Zone).Days.OrderBy(d => d.Date).ToList();

        var first = days[0].Totals.Single();
        Assert.True(first.GoalReached);
        Assert.Equal(At(4, 1), first.GoalReachedUtc);
        Assert.Equal(TimeSpan.FromHours(73), first.Goal);
        Assert.Equal(1, days[0].GoalCount);
        Assert.Equal(1, days[0].GoalsMet);
        foreach (var middle in days.Skip(1).Take(2))
        {
            var t = middle.Totals.Single();
            Assert.Null(t.Goal);
            Assert.False(t.GoalReached);
            Assert.Equal(TimeSpan.FromHours(24), t.Total);
            Assert.Equal(0, middle.GoalCount);
            Assert.Equal(0, middle.GoalsMet);
        }

        // The 4th starts a new, still-empty cycle that has no progress yet (the 73rd hour closed the previous one).
        Assert.Equal(TimeSpan.Zero, days[3].Totals.Single().CycleProgress);
        Assert.Equal(1, days[3].GoalCount);
    }

    [Fact]
    public void PerCycle_TwoCyclesReachedOnOneDay_CountsBoth()
    {
        var def = CycleDef("a", 2, At(9, 0));
        var sessions = new[] { Span(1, "a", At(9, 1), At(9, 6)) };

        var day = DailyStatisticsBuilder.Build(sessions, [def], Now, Zone).Days.Single();

        var t = day.Totals.Single();
        Assert.Equal(2, t.GoalsReachedCount);
        Assert.True(t.GoalReached);
        Assert.Equal(At(9, 3), t.GoalReachedUtc);
        Assert.Equal(TimeSpan.FromHours(1), t.CycleProgress);
        Assert.Equal(1, day.GoalsMet);
        Assert.Equal(1, day.GoalCount);
        // Time after the first reach: 3h of 5h.
        Assert.Equal(3.0 / 5, t.OvertimeShare, 6);
    }

    [Fact]
    public void PerCycle_DaysBeforeAnchor_UseOldRules()
    {
        var def = CycleDef("a", 4, At(10, 12));
        var sessions = new[] { Span(1, "a", At(9, 9), At(9, 14)), Span(2, "a", At(10, 14), At(10, 15)) };

        var days = DailyStatisticsBuilder.Build(sessions, [def], Now, Zone).Days;

        var before = days[1].Totals.Single();
        Assert.Equal(TimeSpan.FromHours(4), before.Goal);
        Assert.True(before.GoalReached); // 5h >= 4h under the each-day rule
        Assert.Equal(1, before.GoalsReachedCount);
        Assert.Null(before.GoalReachedUtc);
        Assert.Null(before.CycleProgress);

        var anchorDay = days[0].Totals.Single();
        Assert.False(anchorDay.GoalReached);
        Assert.Equal(TimeSpan.FromHours(1), anchorDay.CycleProgress);
    }

    [Fact]
    public void PerCycle_DeletedStopwatch_ListedOnlyWithTime()
    {
        var def = CycleDef("a", 2, At(9, 0), deleted: true);
        var sessions = new[] { Span(1, "a", At(9, 1), At(9, 4)) };

        var days = DailyStatisticsBuilder.Build(sessions, [def], Now, Zone).Days;

        var t = days.Single().Totals.Single();
        Assert.True(t.GoalReached);
        Assert.Equal(1, days.Single().GoalCount);
        Assert.Equal(1, days.Single().GoalsMet);
        Assert.Single(days);
    }

    [Fact]
    public void PerCycle_NotDeleted_ListedOnDayWithoutTime()
    {
        var def = CycleDef("a", 4, At(9, 0));
        var other = Def("b", 1);
        // "a" reaches its goal at 02:00 on the 10th, which opens a new cycle dated the 10th; only "b" has time that day.
        var sessions = new[] { Span(1, "a", At(9, 22), At(10, 2)), Span(2, "b", At(10, 5), At(10, 6)) };

        var day = DailyStatisticsBuilder.Build(sessions, [def, other], Now, Zone).Days.Single(d => d.Date == new DateOnly(2026, 3, 10));

        var t = day.Totals.Single(x => x.StopwatchId == "a");
        Assert.Equal(TimeSpan.Zero, t.Total);
        Assert.Equal(TimeSpan.FromHours(4), t.Goal);
        Assert.False(t.GoalReached);
        Assert.Equal(TimeSpan.Zero, t.CycleProgress);
        Assert.Equal(1, day.GoalCount);
    }

    [Fact]
    public void EachDay_Stopwatch_ReportsReachedCountOnly()
    {
        var day = At(11, 9);
        var def = Def("a", 0, goalMinutes: 60);

        var stats = DailyStatisticsBuilder.Build([Sess(1, "a", day, 90)], [def], Now, Zone).Days.Single();

        var t = stats.Totals.Single();
        Assert.Equal(1, t.GoalsReachedCount);
        Assert.Null(t.GoalReachedUtc);
        Assert.Null(t.CycleProgress);
    }

    [Fact]
    public void PerCycle_CancelledCycle_SetsCycleCancelledAndProgress()
    {
        var def = CycleDef("a", 8, At(9, 0));
        var sessions = new[] { Span(1, "a", At(9, 10), At(9, 12)) };
        var resets = new[] { new GoalReset("a", At(10, 2)) };

        var days = DailyStatisticsBuilder.Build(sessions, [def], Now, Zone, null, resets).Days;

        var t = days.Single().Totals.Single();
        Assert.True(t.CycleCancelled);
        Assert.False(t.GoalReached);
        Assert.Equal(TimeSpan.FromHours(2), t.CycleProgress);
    }

    [Fact]
    public void PerCycle_DayOfNewCycleAfterReset_IsNotCancelled()
    {
        var def = CycleDef("a", 8, At(9, 0));
        var sessions = new[] { Span(1, "a", At(10, 10), At(10, 12)) };
        var resets = new[] { new GoalReset("a", At(10, 8)) };

        var days = DailyStatisticsBuilder.Build(sessions, [def], Now, Zone, null, resets).Days;

        var t = days.Single().Totals.Single();
        Assert.False(t.CycleCancelled);
        Assert.Equal(TimeSpan.FromHours(2), t.CycleProgress);
    }
}
