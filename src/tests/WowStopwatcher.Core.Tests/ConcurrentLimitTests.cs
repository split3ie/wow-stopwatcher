using Microsoft.Extensions.Time.Testing;

namespace WowStopwatcher.Core.Tests;

public class ConcurrentLimitTests
{
    private readonly FakeStopwatchRepository _defs = new();
    private readonly FakeSessionRepository _sessions = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

    public ConcurrentLimitTests()
    {
        _defs.Add(new StopwatchDefinition("a", "A", "#111111", null, null, 0));
        _defs.Add(new StopwatchDefinition("b", "B", "#222222", null, null, 1));
        _defs.Add(new StopwatchDefinition("i", "I", "#333333", null, null, 2, IsIndependent: true));
        _defs.Add(new StopwatchDefinition("j", "J", "#444444", null, null, 3, IsIndependent: true));
        _defs.Add(new StopwatchDefinition("k", "K", "#555555", null, null, 4, IsIndependent: true));
        _defs.Add(new StopwatchDefinition("l", "L", "#666666", null, null, 5, IsIndependent: true));
    }

    private StopwatchEngine Create() => new(_defs, _sessions, _time);

    [Fact]
    public void MaxConcurrent_IsThree() => Assert.Equal(3, StopwatchEngine.MaxConcurrent);

    [Fact]
    public void FourthIndependent_IsRefused_WithoutChangedEvent()
    {
        var engine = Create();
        engine.Start("i");
        engine.Start("j");
        engine.Start("k");
        var changed = 0;
        engine.Changed += (_, _) => changed++;

        engine.Start("l");
        engine.Toggle("l");

        Assert.Equal(0, changed);
        Assert.Equal(3, engine.GetSnapshot().RunningSessions.Count);
        Assert.False(engine.GetSnapshot().IsRunning("l"));
    }

    [Fact]
    public void NormalSwitch_AllowedAtThree()
    {
        var engine = Create();
        engine.Start("a");
        engine.Start("i");
        engine.Start("j");

        engine.Start("b");
        Assert.Equal("b", engine.GetSnapshot().RunningId);
        Assert.Equal(3, engine.GetSnapshot().RunningSessions.Count);

        engine.StartNext();
        Assert.Equal("a", engine.GetSnapshot().RunningId);
        Assert.Equal(3, engine.GetSnapshot().RunningSessions.Count);
    }

    [Fact]
    public void NormalStart_WithThreeIndependents_IsRefused_AlsoStartNext()
    {
        var engine = Create();
        engine.Start("i");
        engine.Start("j");
        engine.Start("k");
        var changed = 0;
        engine.Changed += (_, _) => changed++;

        engine.Start("a");
        engine.Toggle("a");
        engine.StartNext();

        Assert.Equal(0, changed);
        Assert.Null(engine.GetSnapshot().RunningId);
        Assert.Equal(3, _sessions.GetOpen().Count);
    }

    [Fact]
    public void StoppingOne_FreesASlot()
    {
        var engine = Create();
        engine.Start("i");
        engine.Start("j");
        engine.Start("k");
        engine.Toggle("j");
        engine.Start("a");
        Assert.Equal("a", engine.GetSnapshot().RunningId);
    }

    [Fact]
    public void CanStart_Values()
    {
        var engine = Create();
        Assert.True(engine.GetSnapshot().CanStart("a"));
        Assert.False(engine.GetSnapshot().CanStart("missing"));

        engine.Start("a");
        engine.Start("i");
        var two = engine.GetSnapshot();
        Assert.True(two.CanStart("j"));
        Assert.True(two.CanStart("b"));

        engine.Start("j");
        var three = engine.GetSnapshot();
        Assert.True(three.CanStart("a"));   // already running
        Assert.True(three.CanStart("b"));   // replaces the running normal one
        Assert.False(three.CanStart("k"));  // would be a fourth
        Assert.True(three.CanStart("i"));   // already running

        engine.Toggle("a");
        var noNormal = engine.GetSnapshot();
        Assert.True(noNormal.CanStart("b"));

        engine.Start("k");
        var threeIndependent = engine.GetSnapshot();
        Assert.False(threeIndependent.CanStart("b"));
        Assert.False(threeIndependent.CanStart("l"));
    }

    [Fact]
    public void Load_WithTooManyOpen_KeepsThreeMostRecent()
    {
        var t = _time.GetUtcNow();
        _sessions.InsertOpen("a", t.AddMinutes(-50));
        _sessions.InsertOpen("i", t.AddMinutes(-40));
        _sessions.InsertOpen("j", t.AddMinutes(-30));
        _sessions.InsertOpen("k", t.AddMinutes(-20));
        var engine = Create();

        var snap = engine.GetSnapshot();
        Assert.Equal(3, snap.RunningSessions.Count);
        Assert.Null(snap.RunningId);
        Assert.False(snap.IsRunning("a"));
        Assert.True(snap.IsRunning("i") && snap.IsRunning("j") && snap.IsRunning("k"));
        Assert.Equal(t, _sessions.Items[0].StopUtc);
    }
}
