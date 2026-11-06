namespace WowStopwatcher.Core.Tests;

public class TodayTimelineBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 10, 8, 0, 0, TimeSpan.Zero);

    private static StopwatchDefinition Def(string id, string name, int order, int? goalMinutes = null) =>
        new(id, name, "#" + id.PadRight(6, '0'), goalMinutes is { } g ? TimeSpan.FromMinutes(g) : null, null, order);

    private static Session Sess(int id, string sw, int startMin, int? stopMin) =>
        new(id, sw, T0.AddMinutes(startMin), stopMin is { } s ? T0.AddMinutes(s) : null);

    [Fact]
    public void NothingLogged_RemainingTakesWholeTrack()
    {
        var timeline = TodayTimelineBuilder.Build([Def("a", "English", 0, 300)], [], T0);

        var segment = Assert.Single(timeline.Segments);
        Assert.Equal(TimelineSegmentKind.Remaining, segment.Kind);
        Assert.Equal(1.0, segment.Width, 6);
        Assert.False(segment.Capped);
        Assert.Equal(0, timeline.NowPosition, 6);
        Assert.Equal("Left to 1 goal", segment.Title);
        Assert.Equal("English 5h", segment.SubText);
    }

    [Fact]
    public void RemainingShare_IsProportionalWhenUnderCap()
    {
        // logged 120, remaining 60 -> 1/3 of the track
        var defs = new[] { Def("a", "A", 0, 180) };
        var timeline = TodayTimelineBuilder.Build(defs, [Sess(1, "a", 0, 120)], T0.AddMinutes(200));

        var remaining = timeline.Segments[^1];
        Assert.Equal(1.0 / 3, remaining.Width, 6);
        Assert.False(timeline.RemainingCapped);
        Assert.Equal(2.0 / 3, timeline.NowPosition, 6);
        Assert.Equal(2.0 / 3, timeline.Segments[0].Width, 6);
    }

    [Fact]
    public void RemainingShare_IsCappedAt35Percent()
    {
        var defs = new[] { Def("a", "A", 0, 600), Def("b", "B", 1, 120) };
        var timeline = TodayTimelineBuilder.Build(defs, [Sess(1, "a", 0, 10)], T0.AddMinutes(20));

        var remaining = timeline.Segments[^1];
        Assert.Equal(0.35, remaining.Width, 6);
        Assert.True(remaining.Capped);
        Assert.Equal("Left to 2 goals · shortened to fit", remaining.Title);
        Assert.Equal("A 9h 50m · B 2h", remaining.SubText);
        Assert.Equal(0.65, timeline.NowPosition, 6);
        Assert.Equal(0.65, timeline.Segments[0].Width, 6);
        Assert.Equal(TimeSpan.FromMinutes(710), timeline.Remaining);
    }

    [Fact]
    public void RemainingSubText_ListsThreeGoalsAndMore()
    {
        var defs = new[] { Def("a", "A", 0, 60), Def("b", "B", 1, 50), Def("c", "C", 2, 40), Def("d", "D", 3, 30), Def("e", "E", 4, 20) };
        var segment = TodayTimelineBuilder.Build(defs, [], T0).Segments[0];
        Assert.Equal("A 1h · B 50m · C 40m · +2 more", segment.SubText);
        Assert.Equal("Left to 5 goals", segment.Title);
    }

    [Fact]
    public void ReachedGoals_ProduceNoRemainingSection()
    {
        var defs = new[] { Def("a", "A", 0, 30) };
        var timeline = TodayTimelineBuilder.Build(defs, [Sess(1, "a", 0, 30)], T0.AddMinutes(40));
        Assert.DoesNotContain(timeline.Segments, s => s.Kind == TimelineSegmentKind.Remaining);
        Assert.Equal(1.0, timeline.NowPosition, 6);
    }

    [Fact]
    public void LivePiece_HasRunningTitle()
    {
        var defs = new[] { Def("a", "English", 0) };
        var timeline = TodayTimelineBuilder.Build(defs, [Sess(1, "a", 0, 10), Sess(2, "a", 20, null)], T0.AddMinutes(30));
        Assert.Equal("English · session", timeline.Segments[0].Title);
        Assert.Equal("English · running", timeline.Segments[1].Title);
        Assert.True(timeline.Segments[1].IsLive);
    }

    [Fact]
    public void Pin_SitsInsidePieceWhereGoalWasCrossed()
    {
        // goal 90m; sessions: 60m, (b 30m), 60m. Goal crossed 30m into the 2nd session of a.
        var defs = new[] { Def("a", "English", 0, 90), Def("b", "B", 1) };
        var sessions = new[] { Sess(1, "a", 0, 60), Sess(2, "b", 60, 90), Sess(3, "a", 90, null) };
        var timeline = TodayTimelineBuilder.Build(defs, sessions, T0.AddMinutes(150));

        var pin = Assert.Single(timeline.Pins);
        Assert.Equal(120.0 / 150, pin.Position, 6); // 120 of 150 logged minutes, nothing remaining
        Assert.Equal("During its 2nd session (running now) · 2h into today's log", pin.SubText);
        Assert.Equal(0, pin.Level);
    }

    [Fact]
    public void Pin_ForFinishedSession_HasNoRunningNote()
    {
        var defs = new[] { Def("a", "A", 0, 30) };
        var timeline = TodayTimelineBuilder.Build(defs, [Sess(1, "a", 0, 60)], T0.AddMinutes(70));
        Assert.Equal("During its 1st session · 30m into today's log", timeline.Pins[0].SubText);
        Assert.Equal(0.5, timeline.Pins[0].Position, 6);
    }

    [Fact]
    public void Pins_CloseTogether_MoveUpLevels()
    {
        // Three goals all crossed in the same minute region of a long track.
        var defs = new[] { Def("a", "A", 0, 100), Def("b", "B", 1, 100), Def("c", "C", 2, 100) };
        var sessions = new[] { Sess(1, "a", 0, 100), Sess(2, "b", 100, 200), Sess(3, "c", 200, 300) };
        var timeline = TodayTimelineBuilder.Build(defs, sessions, T0.AddMinutes(400));

        // positions 1/3, 2/3, 1: far apart -> level 0
        Assert.All(timeline.Pins, p => Assert.Equal(0, p.Level));
        Assert.Equal(0, timeline.MaxPinLevel);

        var d2 = new[] { Def("a", "A", 0, 100), Def("b", "B", 1, 101) };
        var s2 = new[] { Sess(1, "a", 0, 100), Sess(2, "b", 100, 201), Sess(3, "a", 201, 1001) };
        // a crosses at 100/1001, b crosses at 201/1001: 10% apart -> level 0.
        var far = TodayTimelineBuilder.Build(d2, s2, T0.AddMinutes(2000));
        Assert.Equal(0, far.MaxPinLevel);

        var d3 = new[] { Def("a", "A", 0, 100), Def("b", "B", 1, 100), Def("c", "C", 2, 100) };
        var s3 = new[] { Sess(1, "a", 0, 100), Sess(2, "b", 100, 200), Sess(3, "c", 200, 300), Sess(4, "a", 300, 10000) };
        // pins at 100/10000, 200/10000, 300/10000 of the logged part = 1%, 2%, 3% -> overlapping
        var close = TodayTimelineBuilder.Build(d3, s3, T0.AddMinutes(20000));
        Assert.Equal([0, 1, 2], close.Pins.Select(p => p.Level));
        Assert.Equal(2, close.MaxPinLevel);
    }

    [Fact]
    public void Pins_FarEnoughApart_ReuseLowerLevel()
    {
        var defs = new[] { Def("a", "A", 0, 10), Def("b", "B", 1, 10), Def("c", "C", 2, 300) };
        // a at 10/1000, b at 20/1000 -> 1% apart -> level 1; c at 500+... far -> level 0
        var sessions = new[] { Sess(1, "a", 0, 10), Sess(2, "b", 10, 20), Sess(3, "c", 20, 520), Sess(4, "a", 520, 1000) };
        var timeline = TodayTimelineBuilder.Build(defs, sessions, T0.AddMinutes(1100));
        Assert.Equal([0, 1, 0], timeline.Pins.Select(p => p.Level));
    }
}
