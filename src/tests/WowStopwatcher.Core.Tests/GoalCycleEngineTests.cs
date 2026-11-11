using Microsoft.Extensions.Time.Testing;

namespace WowStopwatcher.Core.Tests;

public class GoalCycleEngineTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(2);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("test", Offset, "test", "test");

    // Monday is 2026-03-09; times are local in the +02:00 test zone.
    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new DateTimeOffset(2026, 3, day, hour, minute, 0, Offset).ToUniversalTime();

    private readonly FakeStopwatchRepository _defs = new();
    private readonly FakeSessionRepository _sessions = new();
    private readonly FakeTimeProvider _time = new(At(9, 0));

    public GoalCycleEngineTests()
    {
        _time.SetLocalTimeZone(Zone);
    }

    private StopwatchEngine Create() => new(_defs, _sessions, _time);

    private void AddPerCycle(TimeSpan goal, DateTimeOffset? since) =>
        _defs.Add(new StopwatchDefinition("a", "A", "#111111", goal, null, 0, GoalSinceUtc: since, GoalPerCycle: true));

    // Advances in hour steps (at most) so midnight splits happen on the way.
    private void Advance(StopwatchEngine engine, TimeSpan span)
    {
        while (span > TimeSpan.Zero)
        {
            var step = span > TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : span;
            _time.Advance(step);
            engine.Tick();
            span -= step;
        }
    }

    [Fact]
    public void Load_SetsMissingAnchorToStartOfToday()
    {
        AddPerCycle(TimeSpan.FromHours(8), null);

        Create();

        Assert.Equal(At(9, 0), _defs.Get("a")!.GoalSinceUtc);
    }

    [Fact]
    public void Load_ClearsAnchorWhenNotPerCycle()
    {
        _defs.Add(new StopwatchDefinition("a", "A", "#111111", TimeSpan.FromHours(1), null, 0, GoalSinceUtc: At(9, 0)));
        _defs.Add(new StopwatchDefinition("b", "B", "#222222", null, null, 1, GoalSinceUtc: At(9, 0), GoalPerCycle: true));

        Create();

        Assert.Null(_defs.Get("a")!.GoalSinceUtc);
        Assert.Null(_defs.Get("b")!.GoalSinceUtc);
    }

    [Fact]
    public void Load_LeavesDeletedDefinitionsAlone()
    {
        _defs.Add(new StopwatchDefinition(
            "a", "A", "#111111", TimeSpan.FromHours(1), null, 0, IsDeleted: true, GoalPerCycle: true));
        _defs.Add(new StopwatchDefinition("b", "B", "#222222", null, null, 1, IsDeleted: true, GoalSinceUtc: At(9, 0)));

        Create();

        Assert.Null(_defs.Get("a")!.GoalSinceUtc);
        Assert.Equal(At(9, 0), _defs.Get("b")!.GoalSinceUtc);
    }

    [Fact]
    public void Reload_SetsAndClearsAnchor()
    {
        _defs.Add(new StopwatchDefinition("a", "A", "#111111", null, null, 0));
        var engine = Create();
        Assert.Null(_defs.Get("a")!.GoalSinceUtc);

        _defs.Update(_defs.Get("a")! with { Goal = TimeSpan.FromHours(1), GoalPerCycle = true });
        engine.ReloadDefinitions();
        Assert.Equal(At(9, 0), _defs.Get("a")!.GoalSinceUtc);
        Assert.Equal(At(9, 0), engine.GetSnapshot().Find("a")!.GoalSinceUtc);

        _defs.Update(_defs.Get("a")! with { GoalPerCycle = false });
        engine.ReloadDefinitions();
        Assert.Null(_defs.Get("a")!.GoalSinceUtc);
        Assert.Empty(engine.GetSnapshot().GetGoalCycles("a"));
    }

    [Fact]
    public void OvernightCycle_CarriedBeforeReached_ThenCountsFromReachedInstant()
    {
        AddPerCycle(TimeSpan.FromHours(8), At(9, 20));
        _time.SetUtcNow(At(9, 20));
        var engine = Create();
        engine.Start("a");

        Advance(engine, TimeSpan.FromHours(7)); // Tue 03:00
        var before = engine.GetSnapshot().GetGoalStatus("a")!;
        Assert.False(before.IsReached);
        Assert.Equal(At(9, 20), before.CarriedFromUtc);
        Assert.Equal(TimeSpan.FromHours(7), before.Progress);
        Assert.Equal(TimeSpan.FromHours(1), before.Remaining);

        Advance(engine, TimeSpan.FromHours(1) + TimeSpan.FromMinutes(30)); // Tue 04:30
        var after = engine.GetSnapshot().GetGoalStatus("a")!;
        Assert.False(after.IsReached);
        Assert.Equal(0, after.ReachedCount);
        Assert.Null(after.CarriedFromUtc);
        Assert.Equal(TimeSpan.FromMinutes(30), after.Progress);
        Assert.Equal(TimeSpan.FromHours(7) + TimeSpan.FromMinutes(30), after.Remaining);
    }

    [Fact]
    public void ReachedTwiceInOneDay_CountsBoth()
    {
        AddPerCycle(TimeSpan.FromHours(1), At(9, 0));
        _time.SetUtcNow(At(9, 10));
        var engine = Create();
        engine.Start("a");

        Advance(engine, TimeSpan.FromMinutes(150)); // 12:30
        var status = engine.GetSnapshot().GetGoalStatus("a")!;

        Assert.True(status.IsReached);
        Assert.Equal(2, status.ReachedCount);
        Assert.Equal(TimeSpan.FromMinutes(30), status.OverGoal);
        Assert.Equal(TimeSpan.Zero, status.Remaining);
    }

    [Fact]
    public void EachDayStopwatch_IsUnchanged()
    {
        _defs.Add(new StopwatchDefinition("a", "A", "#111111", TimeSpan.FromHours(1), null, 0));
        _time.SetUtcNow(At(9, 10));
        var engine = Create();
        engine.Start("a");

        Advance(engine, TimeSpan.FromMinutes(30));
        var partial = engine.GetSnapshot().GetGoalStatus("a")!;
        Assert.False(partial.IsReached);
        Assert.Equal(TimeSpan.FromMinutes(30), partial.Remaining);
        Assert.Empty(engine.GetSnapshot().GetGoalCycles("a"));

        Advance(engine, TimeSpan.FromMinutes(60));
        var done = engine.GetSnapshot().GetGoalStatus("a")!;
        Assert.True(done.IsReached);
        Assert.Equal(1, done.ReachedCount);
        Assert.Equal(TimeSpan.FromMinutes(30), done.OverGoal);
        Assert.Null(done.CarriedFromUtc);
        Assert.Equal(TimeSpan.FromMinutes(90), done.Progress);
        Assert.Null(engine.GetSnapshot().GetGoalStatus("none"));
    }

    [Fact]
    public void Tracker_PerCycle_NotifiesOncePerNewCycleAcrossMidnight_NotAtBaseline()
    {
        AddPerCycle(TimeSpan.FromHours(1), At(9, 0));
        _time.SetUtcNow(At(9, 22, 30));
        var engine = Create();
        var tracker = new GoalTracker();
        engine.Start("a");
        Assert.Empty(tracker.Check(engine.GetSnapshot()));

        Advance(engine, TimeSpan.FromHours(1)); // 23:30, first cycle reached
        Assert.Equal("a", Assert.Single(tracker.Check(engine.GetSnapshot())).Id);
        Assert.Empty(tracker.Check(engine.GetSnapshot()));

        Advance(engine, TimeSpan.FromHours(1)); // Tue 00:30, second cycle reached across midnight
        Assert.Equal("a", Assert.Single(tracker.Check(engine.GetSnapshot())).Id);

        Advance(engine, TimeSpan.FromMinutes(10));
        Assert.Empty(tracker.Check(engine.GetSnapshot()));
    }

    [Fact]
    public void Tracker_PerCycle_AlreadyReachedAtFirstCheck_IsNotReported()
    {
        AddPerCycle(TimeSpan.FromHours(1), At(9, 0));
        _time.SetUtcNow(At(9, 10));
        var engine = Create();
        engine.Start("a");
        Advance(engine, TimeSpan.FromHours(3));

        var tracker = new GoalTracker();
        Assert.Empty(tracker.Check(engine.GetSnapshot()));
        Assert.Empty(tracker.Check(engine.GetSnapshot()));
    }

    [Fact]
    public void Tracker_SwitchingToPerCycle_DoesNotReportAlreadyReachedCycles()
    {
        _defs.Add(new StopwatchDefinition("a", "A", "#111111", TimeSpan.FromHours(1), null, 0));
        _time.SetUtcNow(At(9, 10));
        var engine = Create();
        var tracker = new GoalTracker();
        engine.Start("a");
        tracker.Check(engine.GetSnapshot());
        Advance(engine, TimeSpan.FromHours(2));
        Assert.Single(tracker.Check(engine.GetSnapshot())); // each-day goal reached

        _defs.Update(_defs.Get("a")! with { GoalPerCycle = true });
        engine.ReloadDefinitions();

        Assert.Empty(tracker.Check(engine.GetSnapshot()));
        Advance(engine, TimeSpan.FromMinutes(10));
        Assert.Empty(tracker.Check(engine.GetSnapshot()));
        Advance(engine, TimeSpan.FromHours(1));
        Assert.Single(tracker.Check(engine.GetSnapshot())); // a genuinely new cycle is still reported
    }

    [Fact]
    public void Tracker_PerCycle_TwoCyclesInOneCheck_ReportsStopwatchOnce()
    {
        AddPerCycle(TimeSpan.FromHours(1), At(9, 0));
        _time.SetUtcNow(At(9, 10));
        var engine = Create();
        var tracker = new GoalTracker();
        engine.Start("a");
        tracker.Check(engine.GetSnapshot());

        Advance(engine, TimeSpan.FromMinutes(130));

        Assert.Single(tracker.Check(engine.GetSnapshot()));
    }

    [Fact]
    public void Timeline_ShowsPinForYesterdaysCycleReachedToday()
    {
        AddPerCycle(TimeSpan.FromHours(8), At(9, 20));
        _time.SetUtcNow(At(9, 20));
        var engine = Create();
        engine.Start("a");
        Advance(engine, TimeSpan.FromHours(8)); // Tue 04:00, cycle reached
        engine.StopAll();
        Advance(engine, TimeSpan.FromHours(1)); // Tue 05:00

        var timeline = TodayTimelineBuilder.Build(engine.GetSnapshot());

        var pin = Assert.Single(timeline.Pins);
        Assert.Equal("A · goal 8h reached", pin.Title);
        Assert.Equal("Mon's goal, started Mon 20:00 · 4h into today's log", pin.SubText);
        Assert.Equal(timeline.NowPosition, pin.Position, 6);
        Assert.Equal(TimeSpan.FromHours(8), timeline.Remaining);
    }

    [Fact]
    public void Timeline_PinForTodaysCycle_UsesSessionText()
    {
        AddPerCycle(TimeSpan.FromHours(1), At(9, 0));
        _time.SetUtcNow(At(9, 10));
        var engine = Create();
        engine.Start("a");
        Advance(engine, TimeSpan.FromMinutes(90));

        var timeline = TodayTimelineBuilder.Build(engine.GetSnapshot());

        var pin = Assert.Single(timeline.Pins);
        Assert.Equal("During its 1st session (running now) · 1h into today's log", pin.SubText);
    }

    [Fact]
    public void StartNewGoalCycle_ReturnsFalse_WhenNotPerCycleOrUnknown()
    {
        _defs.Add(new StopwatchDefinition("a", "A", "#111111", TimeSpan.FromHours(1), null, 0));
        _defs.Add(new StopwatchDefinition("b", "B", "#222222", null, null, 1));
        _defs.Add(new StopwatchDefinition("c", "C", "#333333", TimeSpan.FromHours(1), null, 2, IsDeleted: true, GoalPerCycle: true));
        var engine = Create();

        Assert.False(engine.StartNewGoalCycle("a"));
        Assert.False(engine.StartNewGoalCycle("b"));
        Assert.False(engine.StartNewGoalCycle("c"));
        Assert.False(engine.StartNewGoalCycle("missing"));
        Assert.Empty(_defs.GoalResets);
    }

    [Fact]
    public void StartNewGoalCycle_PerCycle_CancelsOpenCycleAndZeroesProgress()
    {
        AddPerCycle(TimeSpan.FromHours(8), At(9, 0));
        _time.SetUtcNow(At(9, 10));
        var engine = Create();
        engine.Start("a");
        Advance(engine, TimeSpan.FromHours(2));
        Assert.Equal(TimeSpan.FromHours(2), engine.GetSnapshot().GetGoalStatus("a")!.Progress);
        var raised = 0;
        engine.Changed += (_, _) => raised++;

        Assert.True(engine.StartNewGoalCycle("a"));

        Assert.Equal(1, raised);
        Assert.Equal(new GoalReset("a", At(9, 12)), Assert.Single(_defs.GoalResets));
        var snapshot = engine.GetSnapshot();
        var status = snapshot.GetGoalStatus("a")!;
        Assert.Equal(TimeSpan.Zero, status.Progress);
        Assert.False(status.IsReached);
        var cycles = snapshot.GetGoalCycles("a");
        Assert.Equal(2, cycles.Count);
        Assert.True(cycles[0].IsCancelled);
        Assert.Equal(TimeSpan.FromHours(2), cycles[0].Progress);

        Advance(engine, TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromHours(1), engine.GetSnapshot().GetGoalStatus("a")!.Progress);
    }

    [Fact]
    public void Tracker_AfterReset_NoFalseNotification_ThenNotifiesWhenNewCycleReached()
    {
        AddPerCycle(TimeSpan.FromHours(1), At(9, 0));
        _time.SetUtcNow(At(9, 10));
        var engine = Create();
        var tracker = new GoalTracker();
        tracker.Check(engine.GetSnapshot());
        engine.Start("a");
        Advance(engine, TimeSpan.FromMinutes(30));
        Assert.Empty(tracker.Check(engine.GetSnapshot()));

        engine.StartNewGoalCycle("a");
        Assert.Empty(tracker.Check(engine.GetSnapshot()));

        Advance(engine, TimeSpan.FromMinutes(70));
        Assert.Single(tracker.Check(engine.GetSnapshot()));
    }
}
