namespace WowStopwatcher.Core.Tests;

public class RunningSessionsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 9, 10, 0, 0, TimeSpan.Zero);

    private static Session S(long id, string stopwatchId, int minutes) =>
        new(id, stopwatchId, T0.AddMinutes(minutes), null);

    [Fact]
    public void OpenCount_CountsNormalAndIndependent()
    {
        var open = new RunningSessions(3);
        Assert.Equal(0, open.OpenCount);

        open.Normal = S(1, "a", 0);
        open.SetIndependent("b", S(2, "b", 1));

        Assert.Equal(2, open.OpenCount);
    }

    [Fact]
    public void HasRoomFor_FullSlots_AllowsOnlyNormalReplacingNormal()
    {
        var open = new RunningSessions(2);
        open.Normal = S(1, "a", 0);
        open.SetIndependent("b", S(2, "b", 1));

        Assert.True(open.HasRoomFor(isIndependent: false));
        Assert.False(open.HasRoomFor(isIndependent: true));
    }

    [Fact]
    public void HasRoomFor_FullWithoutNormal_RejectsEverything()
    {
        var open = new RunningSessions(2);
        open.SetIndependent("b", S(1, "b", 0));
        open.SetIndependent("c", S(2, "c", 1));

        Assert.False(open.HasRoomFor(isIndependent: false));
        Assert.False(open.HasRoomFor(isIndependent: true));
    }

    [Fact]
    public void IsRunning_FindsNormalAndIndependent()
    {
        var open = new RunningSessions(3);
        open.Normal = S(1, "a", 0);
        open.SetIndependent("b", S(2, "b", 1));

        Assert.True(open.IsRunning("a"));
        Assert.True(open.IsRunning("b"));
        Assert.False(open.IsRunning("c"));
    }

    [Fact]
    public void AllOpen_ReturnsNormalFirstThenIndependent()
    {
        var open = new RunningSessions(3);
        open.SetIndependent("b", S(2, "b", 1));
        open.Normal = S(1, "a", 0);

        Assert.Equal([1L, 2L], open.AllOpen().Select(s => s.Id));
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var open = new RunningSessions(3);
        open.Normal = S(1, "a", 0);
        open.SetIndependent("b", S(2, "b", 1));

        open.Clear();

        Assert.Null(open.Normal);
        Assert.Empty(open.Independent);
        Assert.Equal(0, open.OpenCount);
    }

    [Fact]
    public void TryRemoveIndependent_RemovesOnlyThatOne()
    {
        var open = new RunningSessions(3);
        open.SetIndependent("b", S(2, "b", 1));

        Assert.True(open.TryRemoveIndependent("b", out var removed));
        Assert.Equal(2, removed.Id);
        Assert.False(open.TryRemoveIndependent("b", out _));
    }

    [Fact]
    public void SessionsOverLimit_ReturnsOldestBeyondLimit()
    {
        var open = new RunningSessions(2);
        open.Normal = S(1, "a", 0);
        open.SetIndependent("b", S(2, "b", 5));
        open.SetIndependent("c", S(3, "c", 10));

        Assert.Equal([1L], open.SessionsOverLimit().Select(s => s.Id));
    }

    [Fact]
    public void SessionsOverLimit_SameStart_DropsLowerId()
    {
        var open = new RunningSessions(1);
        open.Normal = S(1, "a", 0);
        open.SetIndependent("b", S(2, "b", 0));

        Assert.Equal([1L], open.SessionsOverLimit().Select(s => s.Id));
    }

    [Fact]
    public void SessionsOverLimit_WithinLimit_IsEmpty()
    {
        var open = new RunningSessions(3);
        open.Normal = S(1, "a", 0);

        Assert.Empty(open.SessionsOverLimit());
    }
}
