namespace WowStopwatcher.Core.Tests;

public class CombinedTimelineTests
{
    private static DaySegment Seg(double width, int minutes, params string[] ids) =>
        new(ids[0], "#111111", TimeSpan.FromMinutes(minutes), width, [.. ids.Select(id => new TimelineLane(id, "#111111", false))]);

    [Fact]
    public void Empty_ReturnsNothing()
    {
        Assert.Empty(CombinedTimeline.Build([]));
    }

    [Fact]
    public void Interleaved_GroupsSameStopwatchTogether()
    {
        var pieces = CombinedTimeline.Build([Seg(0.1, 6, "a"), Seg(0.2, 12, "b"), Seg(0.3, 18, "a"), Seg(0.4, 24, "b")]);

        Assert.Equal([0.0, 0.4, 0.1, 0.6], pieces.Select(p => Math.Round(p.CombinedStart, 6)));
        Assert.Equal([0.0, 0.1, 0.3, 0.6], pieces.Select(p => Math.Round(p.NormalStart, 6)));
        Assert.Equal([0, 1, 0, 1], pieces.Select(p => p.Group));
    }

    [Fact]
    public void Overlap_IsSplitIntoGroupsPerStopwatch()
    {
        var pieces = CombinedTimeline.Build([Seg(0.2, 12, "a"), Seg(0.1, 6, "b", "a"), Seg(0.3, 18, "c")]);

        Assert.Equal(4, pieces.Count);
        Assert.Equal(["a", "b", "a", "c"], pieces.Select(p => p.StopwatchId));
        Assert.Equal([0, 1, 0, 2], pieces.Select(p => p.Group));
        Assert.Equal(TimeSpan.FromMinutes(18), pieces[0].GroupDuration);
        Assert.Equal(TimeSpan.FromMinutes(6), pieces[1].GroupDuration);
        Assert.Equal(0.3, pieces[0].GroupWidth, 6);
        Assert.Equal([0.0, 0.3, 0.2, 0.4], pieces.Select(p => Math.Round(p.CombinedStart, 6)));
    }

    [Fact]
    public void LaneIndexAndCount_DescribeThePieceInItsSegment()
    {
        var pieces = CombinedTimeline.Build([Seg(0.2, 1, "a"), Seg(0.1, 1, "b", "a", "c")]);

        Assert.Equal([0, 0, 1, 2], pieces.Select(p => p.LaneIndex));
        Assert.Equal([1, 3, 3, 3], pieces.Select(p => p.LaneCount));
        Assert.Equal([0, 1, 1, 1], pieces.Select(p => p.SegmentIndex));
        Assert.All(pieces.Skip(1), p => Assert.Equal(0.2, p.NormalStart, 6));
    }

    [Fact]
    public void GroupDurationAndWidth_AreSums()
    {
        var pieces = CombinedTimeline.Build([Seg(0.1, 5, "a"), Seg(0.2, 10, "b"), Seg(0.3, 15, "a")]);

        Assert.Equal(0.4, pieces[0].GroupWidth, 6);
        Assert.Equal(0.4, pieces[2].GroupWidth, 6);
        Assert.Equal(TimeSpan.FromMinutes(20), pieces[0].GroupDuration);
        Assert.Equal(TimeSpan.FromMinutes(10), pieces[1].GroupDuration);
    }

    [Fact]
    public void IsGroupEnd_OnlyOnLastPieceOfGroup()
    {
        var pieces = CombinedTimeline.Build([Seg(0.1, 1, "a"), Seg(0.1, 1, "b"), Seg(0.1, 1, "a")]);

        Assert.Equal([false, true, true], pieces.Select(p => p.IsGroupEnd));
    }

    [Fact]
    public void TotalWidth_IsLaneWeighted()
    {
        var segments = new[] { Seg(0.1, 1, "a"), Seg(0.25, 1, "b"), Seg(0.3, 1, "a"), Seg(0.15, 1, "a", "b") };
        var pieces = CombinedTimeline.Build(segments);

        var end = pieces.Max(p => p.CombinedStart + p.Width);
        Assert.Equal(segments.Sum(s => s.WidthFraction * s.Lanes.Count), end, 6);
    }

    [Fact]
    public void NoLanes_FallsBackToSegmentStopwatch()
    {
        DaySegment NoLanes(string id) => new(id, "#222222", TimeSpan.FromMinutes(1), 0.1, []);
        var pieces = CombinedTimeline.Build([NoLanes("a"), NoLanes("b"), NoLanes("a")]);

        Assert.Equal([0, 1, 0], pieces.Select(p => p.Group));
        Assert.All(pieces, p => Assert.Equal(1, p.LaneCount));
        Assert.All(pieces, p => Assert.Equal("#222222", p.Color));
    }
}
