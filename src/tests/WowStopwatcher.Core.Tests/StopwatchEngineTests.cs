using Microsoft.Extensions.Time.Testing;

namespace WowStopwatcher.Core.Tests;

public class StopwatchEngineTests
{
    private readonly FakeStopwatchRepository _defs = new();
    private readonly FakeSessionRepository _sessions = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

    public StopwatchEngineTests()
    {
        _defs.Add(new StopwatchDefinition("a", "A", "#111111", TimeSpan.FromHours(1), null, 0));
        _defs.Add(new StopwatchDefinition("b", "B", "#222222", null, null, 1));
        _defs.Add(new StopwatchDefinition("c", "C", "#333333", null, null, 2));
    }

    private StopwatchEngine Create() => new(_defs, _sessions, _time);

    [Fact]
    public void Start_StopsPreviousAndKeepsSingleRunning()
    {
        var engine = Create();
        engine.Start("a");
        _time.Advance(TimeSpan.FromMinutes(10));
        engine.Start("b");

        var snap = engine.GetSnapshot();
        Assert.Equal("b", snap.RunningId);
        Assert.Single(_sessions.GetOpen());
        Assert.Equal(TimeSpan.FromMinutes(10), snap.GetTodayTotal("a"));
    }

    [Fact]
    public void Toggle_StartsThenStops()
    {
        var engine = Create();
        engine.Toggle("a");
        Assert.True(engine.GetSnapshot().IsRunning("a"));
        _time.Advance(TimeSpan.FromSeconds(5));
        engine.Toggle("a");
        var snap = engine.GetSnapshot();
        Assert.Null(snap.RunningId);
        Assert.Equal(TimeSpan.FromSeconds(5), snap.GetTodayTotal("a"));
    }

    [Fact]
    public void StopAll_ClosesRunningSession()
    {
        var engine = Create();
        engine.Start("a");
        _time.Advance(TimeSpan.FromSeconds(30));
        engine.StopAll();
        Assert.Empty(_sessions.GetOpen());
        Assert.Null(engine.GetSnapshot().RunningId);
    }

    [Fact]
    public void Changed_IsRaisedOnStart()
    {
        var engine = Create();
        var count = 0;
        engine.Changed += (_, _) => count++;
        engine.Start("a");
        Assert.Equal(1, count);
    }

    [Fact]
    public void CurrentValue_IsRunningElapsedElseLastFinishedElseZero()
    {
        var engine = Create();
        Assert.Equal(TimeSpan.Zero, engine.GetSnapshot().GetCurrentValue("a"));
        engine.Start("a");
        _time.Advance(TimeSpan.FromSeconds(20));
        engine.Start("b");
        _time.Advance(TimeSpan.FromSeconds(7));
        engine.Start("a");
        _time.Advance(TimeSpan.FromSeconds(3));

        var snap = engine.GetSnapshot();
        Assert.Equal(TimeSpan.FromSeconds(3), snap.GetCurrentValue("a"));
        Assert.Equal(TimeSpan.FromSeconds(7), snap.GetCurrentValue("b"));
        Assert.Equal(TimeSpan.FromSeconds(30), snap.GrandTotal);
        Assert.Equal(TimeSpan.FromSeconds(23), snap.GetTodayTotal("a"));
    }

    [Fact]
    public void StartNext_AdvancesInOrderAndWraps()
    {
        var engine = Create();
        engine.Start("a");
        engine.StartNext();
        Assert.Equal("b", engine.GetSnapshot().RunningId);
        engine.StartNext();
        Assert.Equal("c", engine.GetSnapshot().RunningId);
        engine.StartNext();
        Assert.Equal("a", engine.GetSnapshot().RunningId);
    }

    [Fact]
    public void StartNext_SkipsDeleted()
    {
        _defs.SoftDelete("b");
        var engine = Create();
        engine.Start("a");
        engine.StartNext();
        Assert.Equal("c", engine.GetSnapshot().RunningId);
    }

    [Fact]
    public void StartNext_WhenIdle_StartsLastUsedElseFirst()
    {
        var engine = Create();
        engine.StartNext();
        Assert.Equal("a", engine.GetSnapshot().RunningId);

        engine.Start("c");
        _time.Advance(TimeSpan.FromSeconds(1));
        engine.StopAll();
        _time.Advance(TimeSpan.FromSeconds(1));
        engine.StartNext();
        Assert.Equal("c", engine.GetSnapshot().RunningId);
    }

    [Fact]
    public void ResetToday_DeletesSessionsAndStopsRunning()
    {
        var engine = Create();
        engine.Start("a");
        _time.Advance(TimeSpan.FromMinutes(5));
        engine.Start("b");
        _time.Advance(TimeSpan.FromMinutes(5));

        engine.ResetToday("b");
        var snap = engine.GetSnapshot();
        Assert.Null(snap.RunningId);
        Assert.Equal(TimeSpan.Zero, snap.GetTodayTotal("b"));
        Assert.Equal(TimeSpan.FromMinutes(5), snap.GetTodayTotal("a"));
    }

    [Fact]
    public void ResetAllToday_ClearsEverything()
    {
        var engine = Create();
        engine.Start("a");
        _time.Advance(TimeSpan.FromMinutes(5));
        engine.Start("b");
        engine.ResetAllToday();
        var snap = engine.GetSnapshot();
        Assert.Null(snap.RunningId);
        Assert.Empty(snap.TodaySessions);
        Assert.Empty(_sessions.Items);
    }

    [Fact]
    public void Load_ResumesOpenSessionAfterCrash()
    {
        _sessions.InsertOpen("b", _time.GetUtcNow());
        _time.Advance(TimeSpan.FromMinutes(2));

        var engine = Create();
        var snap = engine.GetSnapshot();
        Assert.Equal("b", snap.RunningId);
        Assert.Equal(TimeSpan.FromMinutes(2), snap.RunningElapsed);
    }

    [Fact]
    public void Tick_SplitsSessionAtLocalMidnight()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(2), "t", "t"));
        _time.SetUtcNow(new DateTimeOffset(2026, 3, 10, 21, 0, 0, TimeSpan.Zero)); // 23:00 local
        var engine = Create();
        engine.Start("a");
        _time.Advance(TimeSpan.FromHours(2)); // 01:00 local next day
        engine.Tick();

        var snap = engine.GetSnapshot();
        Assert.Equal(new DateOnly(2026, 3, 11), snap.Today);
        Assert.Equal("a", snap.RunningId);
        Assert.Equal(TimeSpan.FromHours(1), snap.RunningElapsed);
        Assert.Equal(TimeSpan.FromHours(1), snap.GetTodayTotal("a"));
        var all = _sessions.Items;
        Assert.Equal(2, all.Count);
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 22, 0, 0, TimeSpan.Zero), all[0].StopUtc);
        Assert.Equal(all[0].StopUtc, all[1].StartUtc);
        Assert.Single(_sessions.GetOpen());
    }

    [Fact]
    public void Load_SplitsOpenSessionFromYesterday()
    {
        _sessions.InsertOpen("a", new DateTimeOffset(2026, 3, 9, 23, 0, 0, TimeSpan.Zero));
        var engine = Create();
        var snap = engine.GetSnapshot();
        Assert.Equal("a", snap.RunningId);
        Assert.Equal(TimeSpan.FromHours(12), snap.RunningElapsed);
        Assert.Equal(2, _sessions.Items.Count);
    }

    [Fact]
    public void ReloadDefinitions_StopsRunningIfDeleted()
    {
        var engine = Create();
        engine.Start("b");
        _time.Advance(TimeSpan.FromMinutes(1));
        _defs.SoftDelete("b");
        engine.ReloadDefinitions();

        var snap = engine.GetSnapshot();
        Assert.Null(snap.RunningId);
        Assert.Empty(_sessions.GetOpen());
        Assert.Equal(TimeSpan.FromMinutes(1), snap.GetTodayTotal("b"));
        Assert.DoesNotContain(snap.Stopwatches, s => s.Id == "b");
    }

    [Fact]
    public void Start_IgnoresDeletedStopwatch()
    {
        _defs.SoftDelete("b");
        var engine = Create();
        engine.Start("b");
        Assert.Null(engine.GetSnapshot().RunningId);
    }
}
