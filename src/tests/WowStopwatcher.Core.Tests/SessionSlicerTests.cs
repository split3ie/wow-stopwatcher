namespace WowStopwatcher.Core.Tests;

public class SessionSlicerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 10, 8, 0, 0, TimeSpan.Zero);

    private static readonly StopwatchDefinition[] Defs =
    [
        new("n", "N", "#111111", null, null, 0),
        new("m", "M", "#222222", null, null, 1),
        new("i", "I", "#333333", null, null, 2, IsIndependent: true),
        new("j", "J", "#444444", null, null, 3, IsIndependent: true),
    ];

    private static Session S(int id, string sw, int startMin, int? stopMin) =>
        new(id, sw, T0.AddMinutes(startMin), stopMin is { } s ? T0.AddMinutes(s) : null);

    private static string[] Ids(SessionSlice slice) => slice.Lanes.Select(l => l.StopwatchId).ToArray();

    [Fact]
    public void Nested_IndependentInsideNormal_TotalIsOuter()
    {
        var slices = SessionSlicer.Build([S(1, "n", 0, 5), S(2, "i", 2, 4)], Defs, T0.AddHours(1));

        Assert.Equal(3, slices.Count);
        Assert.Equal([new[] { "n" }, ["n", "i"], ["n"]], slices.Select(Ids));
        Assert.Equal([2, 2, 1], slices.Select(s => (int)s.Duration.TotalMinutes));
        Assert.Equal(TimeSpan.FromMinutes(5), SessionSlicer.Total(slices));
    }

    [Fact]
    public void PartialOverlap_IndependentFirst_NormalIsFirstLane()
    {
        var slices = SessionSlicer.Build([S(1, "i", 0, 2), S(2, "n", 1, 4)], Defs, T0.AddHours(1));

        Assert.Equal([new[] { "i" }, ["n", "i"], ["n"]], slices.Select(Ids));
        Assert.Equal(TimeSpan.FromMinutes(4), SessionSlicer.Total(slices));
    }

    [Fact]
    public void TwoIndependentsAndNormal_LaneOrderIsNormalThenIndependentBySortOrder()
    {
        var slices = SessionSlicer.Build([S(1, "j", 0, 3), S(2, "i", 0, 3), S(3, "n", 0, 3)], Defs, T0.AddHours(1));

        var slice = Assert.Single(slices);
        Assert.Equal(["n", "i", "j"], Ids(slice));
        Assert.Equal("#111111", slice.Lanes[0].Color);
        Assert.Equal(TimeSpan.FromMinutes(3), slice.Duration);
    }

    [Fact]
    public void OpenSession_IsMeasuredToNow_AndLaneIsLive()
    {
        var slices = SessionSlicer.Build([S(1, "n", 0, 10), S(2, "i", 5, null)], Defs, T0.AddMinutes(20));

        Assert.Equal(TimeSpan.FromMinutes(20), SessionSlicer.Total(slices));
        Assert.False(slices[0].IsLive);
        Assert.True(slices[^1].IsLive);
        Assert.True(slices[^1].Lanes.Single().IsLive);
        Assert.False(slices[1].Lanes.Single(l => l.StopwatchId == "n").IsLive);
    }

    [Fact]
    public void AdjacentSlicesWithSameStopwatches_Merge()
    {
        var slices = SessionSlicer.Build([S(1, "n", 0, 5), S(2, "n", 5, 9)], Defs, T0.AddHours(1));

        var slice = Assert.Single(slices);
        Assert.Equal(T0, slice.StartUtc);
        Assert.Equal(TimeSpan.FromMinutes(9), slice.Duration);
    }

    [Fact]
    public void Gaps_AreOmitted_AndUnknownStopwatchesIgnored()
    {
        var slices = SessionSlicer.Build([S(1, "n", 0, 2), S(2, "n", 10, 12), S(3, "ghost", 0, 30)], Defs, T0.AddHours(1));

        Assert.Equal(2, slices.Count);
        Assert.Equal(T0.AddMinutes(10), slices[1].StartUtc);
        Assert.Equal(TimeSpan.FromMinutes(4), SessionSlicer.Total(slices));
    }

    [Fact]
    public void ExampleOne_IndependentInsideNormal_TotalFiveMinutes()
    {
        var slices = SessionSlicer.Build([S(1, "n", 0, 5), S(2, "i", 2, 4)], Defs, T0);
        Assert.Equal(TimeSpan.FromMinutes(5), SessionSlicer.Total(slices));
    }

    [Fact]
    public void ExampleTwo_IndependentThenNormalOverlapping_TotalFourMinutes()
    {
        var slices = SessionSlicer.Build([S(1, "i", 0, 2), S(2, "n", 1, 4)], Defs, T0);
        Assert.Equal(TimeSpan.FromMinutes(4), SessionSlicer.Total(slices));
    }
}
