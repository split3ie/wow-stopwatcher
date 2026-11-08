using Microsoft.Extensions.Time.Testing;

namespace WowStopwatcher.Core.Tests;

public class GoalTrackerTests
{
    private readonly FakeStopwatchRepository _defs = new();
    private readonly FakeSessionRepository _sessions = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

    public GoalTrackerTests()
    {
        _defs.Add(new StopwatchDefinition("a", "A", "#111111", TimeSpan.FromHours(1), null, 0));
        _defs.Add(new StopwatchDefinition("b", "B", "#222222", null, null, 1));
    }

    private StopwatchEngine Create() => new(_defs, _sessions, _time);

    [Fact]
    public void FirstCheck_OnlyBaseline_NotReported()
    {
        var engine = Create();
        engine.Start("a");
        _time.Advance(TimeSpan.FromHours(2));

        var tracker = new GoalTracker();
        Assert.Empty(tracker.Check(engine.GetSnapshot()));
        Assert.Empty(tracker.Check(engine.GetSnapshot()));
    }

    [Fact]
    public void Crossing_IsReportedOnceAndNotAgain()
    {
        var engine = Create();
        var tracker = new GoalTracker();
        engine.Start("a");
        Assert.Empty(tracker.Check(engine.GetSnapshot()));

        _time.Advance(TimeSpan.FromMinutes(59));
        Assert.Empty(tracker.Check(engine.GetSnapshot()));

        _time.Advance(TimeSpan.FromMinutes(1));
        var reached = tracker.Check(engine.GetSnapshot());
        Assert.Equal("a", Assert.Single(reached).Id);

        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.Empty(tracker.Check(engine.GetSnapshot()));
    }

    [Fact]
    public void ResetThenReachAgain_IsReportedAgain()
    {
        var engine = Create();
        var tracker = new GoalTracker();
        engine.Start("a");
        tracker.Check(engine.GetSnapshot());
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Single(tracker.Check(engine.GetSnapshot()));

        engine.ResetToday("a");
        Assert.Empty(tracker.Check(engine.GetSnapshot()));

        engine.Start("a");
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Single(tracker.Check(engine.GetSnapshot()));
    }

    [Fact]
    public void StopwatchWithoutGoal_IsIgnored()
    {
        var engine = Create();
        var tracker = new GoalTracker();
        engine.Start("b");
        tracker.Check(engine.GetSnapshot());
        _time.Advance(TimeSpan.FromHours(10));
        Assert.Empty(tracker.Check(engine.GetSnapshot()));
    }

    [Fact]
    public void NewDay_ClearsReachedSet()
    {
        var engine = Create();
        var tracker = new GoalTracker();
        engine.Start("a");
        tracker.Check(engine.GetSnapshot());
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Single(tracker.Check(engine.GetSnapshot()));

        var tomorrow = engine.GetSnapshot().Today.AddDays(1);
        while (engine.GetSnapshot().Today < tomorrow)
        {
            _time.Advance(TimeSpan.FromHours(1));
            engine.Tick();
        }

        var afterMidnight = engine.GetSnapshot();
        Assert.True(afterMidnight.GetTodayTotal("a") < TimeSpan.FromHours(1));
        Assert.Empty(tracker.Check(afterMidnight));

        _time.Advance(TimeSpan.FromHours(1));
        engine.Tick();
        Assert.Single(tracker.Check(engine.GetSnapshot()));
    }
}
