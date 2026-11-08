namespace WowStopwatcher.Core.Tests;

public class OverlapBuildersTests
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 10, 8, 0, 0, TimeSpan.Zero);

    private static Session S(int id, string sw, int startMin, int? stopMin) =>
        new(id, sw, T0.AddMinutes(startMin), stopMin is { } s ? T0.AddMinutes(s) : null);

    [Fact]
    public void Timeline_OverlapProducesLaneSegments_AndLoggedIsUnion()
    {
        StopwatchDefinition[] defs =
        [
            new("n", "English", "#111111", null, null, 0),
            new("i", "Walking", "#333333", null, null, 1, IsIndependent: true),
        ];
        var timeline = TodayTimelineBuilder.Build(defs, [S(1, "n", 0, 60), S(2, "i", 20, 40)], T0.AddHours(3));

        Assert.Equal(TimeSpan.FromMinutes(60), timeline.Logged);
        Assert.Equal(3, timeline.Segments.Count);
        var middle = timeline.Segments[1];
        Assert.Equal(["n", "i"], middle.Lanes.Select(l => l.StopwatchId));
        Assert.Equal("n", middle.StopwatchId);
        Assert.Equal("English + Walking · together", middle.Title);
        Assert.Equal("English · session", timeline.Segments[0].Title);
        Assert.Single(timeline.Segments[0].Lanes);
        Assert.Equal(1.0 / 3, timeline.Segments[0].Width, 6);
        Assert.Equal(1.0 / 3, middle.Width, 6);
        Assert.Equal(1.0, timeline.Segments.Sum(s => s.Width), 6);
    }

    [Fact]
    public void Timeline_IndependentGoalsAndPinsAreIncluded_WithFullOwnTotal()
    {
        StopwatchDefinition[] defs =
        [
            new("n", "A", "#111111", null, null, 0),
            new("i", "Walk", "#333333", TimeSpan.FromMinutes(30), null, 1, IsIndependent: true),
        ];

        // Walk runs 10..50 (40 min) inside A's 0..60; goal 30 min reached at minute 40 of the clock.
        var timeline = TodayTimelineBuilder.Build(defs, [S(1, "n", 0, 60), S(2, "i", 10, 50)], T0.AddHours(3));
        var pin = Assert.Single(timeline.Pins);
        Assert.Equal("i", pin.StopwatchId);
        Assert.Equal(40.0 / 60, pin.Position, 6);
        Assert.Contains("1st session", pin.SubText);
        Assert.Contains("40m into today's log", pin.SubText);
        Assert.Empty(timeline.RemainingGoals);

        // Not reached yet: remaining goal shows.
        var partial = TodayTimelineBuilder.Build(defs, [S(1, "n", 0, 60), S(2, "i", 10, 20)], T0.AddHours(3));
        Assert.Equal("i", Assert.Single(partial.RemainingGoals).StopwatchId);
        Assert.Equal(TimeSpan.FromMinutes(20), partial.Remaining);
    }

    [Fact]
    public void Timeline_PinOrdinalCountsSessionsNotSlices()
    {
        StopwatchDefinition[] defs =
        [
            new("n", "A", "#111111", null, null, 0),
            new("i", "Walk", "#333333", TimeSpan.FromMinutes(20), null, 1, IsIndependent: true),
        ];

        // Walk session 1: 0..10 (alone), session 2: 20..60 which spans several slices because A overlaps part of it.
        var sessions = new[] { S(1, "i", 0, 10), S(2, "i", 20, 60), S(3, "n", 30, 40) };
        var timeline = TodayTimelineBuilder.Build(defs, sessions, T0.AddHours(3));

        var pin = Assert.Single(timeline.Pins);
        Assert.Contains("2nd session", pin.SubText);
    }

    [Fact]
    public void Statistics_DayTotalIsUnion_LanesPresent_SharesClamped()
    {
        StopwatchDefinition[] defs =
        [
            new("a", "A", "#111111", null, null, 0),
            new("i", "I", "#333333", null, null, 1, IsIndependent: true),
        ];
        var day = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);
        var sessions = new[]
        {
            new Session(1, "a", day, day.AddMinutes(60)),
            new Session(2, "i", day.AddMinutes(20), day.AddMinutes(80)),
        };

        var result = DailyStatisticsBuilder.Build(sessions, defs, day.AddDays(1), TimeZoneInfo.Utc);

        var stats = Assert.Single(result.Days);
        Assert.Equal(TimeSpan.FromMinutes(80), stats.Total);
        Assert.Equal(3, stats.Segments.Count);
        Assert.Equal(["a", "i"], stats.Segments[1].Lanes.Select(l => l.StopwatchId));
        Assert.Equal(TimeSpan.FromMinutes(40), stats.Segments[1].Duration);
        Assert.Equal(2, stats.SessionCount);
        Assert.Equal(TimeSpan.FromMinutes(80), stats.Segments.Aggregate(TimeSpan.Zero, (x, s) => x + s.Duration));

        var a = stats.Totals.Single(t => t.StopwatchId == "a");
        var i = stats.Totals.Single(t => t.StopwatchId == "i");
        Assert.Equal(TimeSpan.FromMinutes(60), a.Total);
        Assert.Equal(TimeSpan.FromMinutes(60), i.Total);
        Assert.Equal(0.75, a.Share, 6);
        Assert.True(i.IsIndependent);
        Assert.InRange(i.Share, 0, 1);
    }

    [Fact]
    public void Statistics_ScaleUsesUnionNotSum()
    {
        StopwatchDefinition[] defs =
        [
            new("a", "A", "#111111", null, null, 0),
            new("i", "I", "#333333", null, null, 1, IsIndependent: true),
        ];
        var day = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);
        // Two fully overlapping 6h sessions: sum 12h would push the scale to 720, union 6h keeps 540.
        var sessions = new[]
        {
            new Session(1, "a", day, day.AddHours(6)),
            new Session(2, "i", day, day.AddHours(6)),
        };
        var result = DailyStatisticsBuilder.Build(sessions, defs, day.AddDays(1), TimeZoneInfo.Utc);
        Assert.Equal(DailyStatisticsBuilder.MinScaleMinutes, result.ScaleMinutes);
    }

    [Fact]
    public void Statistics_DayWithOnlyIndependentSessions_StillAppearsWithBar()
    {
        StopwatchDefinition[] defs = [new("i", "I", "#333333", null, null, 0, IsIndependent: true)];
        var day = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);
        var result = DailyStatisticsBuilder.Build([new Session(1, "i", day, day.AddHours(1))], defs, day.AddDays(1), TimeZoneInfo.Utc);

        var stats = Assert.Single(result.Days);
        Assert.Single(stats.Segments);
        Assert.Equal(TimeSpan.FromHours(1), stats.Total);
        Assert.Equal(1.0, Assert.Single(stats.Totals).Share, 6);
    }
}
