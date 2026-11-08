using Microsoft.Extensions.Time.Testing;

namespace WowStopwatcher.Core.Tests;

public class IndependentStopwatchTests
{
    private readonly FakeStopwatchRepository _defs = new();
    private readonly FakeSessionRepository _sessions = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

    public IndependentStopwatchTests()
    {
        _defs.Add(new StopwatchDefinition("a", "A", "#111111", null, null, 0));
        _defs.Add(new StopwatchDefinition("b", "B", "#222222", null, null, 1));
        _defs.Add(new StopwatchDefinition("i", "I", "#333333", null, null, 2, IsIndependent: true));
        _defs.Add(new StopwatchDefinition("j", "J", "#444444", null, null, 3, IsIndependent: true));
    }

    private StopwatchEngine Create() => new(_defs, _sessions, _time);

    [Fact]
    public void Independent_RunsAlongsideNormal_AndSeveralAtOnce()
    {
        var engine = Create();
        engine.Start("a");
        engine.Start("i");
        engine.Start("j");

        var snap = engine.GetSnapshot();
        Assert.Equal("a", snap.RunningId);
        Assert.Equal(3, snap.RunningSessions.Count);
        Assert.True(snap.IsAnyRunning);
        Assert.True(snap.IsRunning("i"));
        Assert.True(snap.IsRunning("j"));
        Assert.Equal(3, _sessions.GetOpen().Count);
    }

    [Fact]
    public void StartingNormal_DoesNotStopIndependent_AndStartingIndependent_DoesNotStopNormal()
    {
        var engine = Create();
        engine.Start("i");
        engine.Start("a");
        _time.Advance(TimeSpan.FromMinutes(5));
        engine.Start("b");
        engine.Start("j");

        var snap = engine.GetSnapshot();
        Assert.Equal("b", snap.RunningId);
        Assert.True(snap.IsRunning("i"));
        Assert.True(snap.IsRunning("j"));
        Assert.False(snap.IsRunning("a"));
    }

    [Fact]
    public void Toggle_Independent_OnlyAffectsItself()
    {
        var engine = Create();
        engine.Start("a");
        engine.Toggle("i");
        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(2), engine.GetSnapshot().GetCurrentValue("i"));
        engine.Toggle("i");

        var snap = engine.GetSnapshot();
        Assert.Equal("a", snap.RunningId);
        Assert.False(snap.IsRunning("i"));
        Assert.Equal(TimeSpan.FromMinutes(2), snap.GetCurrentValue("i"));
    }

    [Fact]
    public void StopAll_StopsNormalAndIndependent()
    {
        var engine = Create();
        engine.Start("a");
        engine.Start("i");
        engine.Start("j");
        _time.Advance(TimeSpan.FromMinutes(1));
        engine.StopAll();

        Assert.Empty(_sessions.GetOpen());
        Assert.False(engine.GetSnapshot().IsAnyRunning);
    }

    [Fact]
    public void StartNext_SkipsIndependent_AndLeavesThemRunning()
    {
        var engine = Create();
        engine.Start("i");
        engine.StartNext();
        Assert.Equal("a", engine.GetSnapshot().RunningId);
        engine.StartNext();
        Assert.Equal("b", engine.GetSnapshot().RunningId);
        engine.StartNext();

        var snap = engine.GetSnapshot();
        Assert.Equal("a", snap.RunningId);
        Assert.True(snap.IsRunning("i"));
    }

    [Fact]
    public void StartNext_WithOnlyIndependent_DoesNothing()
    {
        _defs.Items.RemoveAll(d => !d.IsIndependent);
        var engine = Create();
        var count = 0;
        engine.Changed += (_, _) => count++;
        engine.StartNext();
        Assert.Equal(0, count);
        Assert.False(engine.GetSnapshot().IsAnyRunning);
    }

    [Fact]
    public void LastUsedId_IgnoresIndependent()
    {
        var engine = Create();
        engine.Start("b");
        _time.Advance(TimeSpan.FromMinutes(1));
        engine.Start("i");
        _time.Advance(TimeSpan.FromMinutes(1));
        engine.StopAll();
        Assert.Equal("b", engine.GetSnapshot().LastUsedId);

        engine.StartNext(); // nothing normal runs -> last used normal
        Assert.Equal("b", engine.GetSnapshot().RunningId);

        // On load as well: the latest session overall is independent.
        engine.StopAll();
        engine.Start("i");
        var reloaded = Create();
        Assert.Equal("b", reloaded.GetSnapshot().LastUsedId);
    }

    [Fact]
    public void ResetToday_ClearsIndependentOpenSession()
    {
        var engine = Create();
        engine.Start("a");
        engine.Start("i");
        engine.ResetToday("i");
        var snap = engine.GetSnapshot();
        Assert.False(snap.IsRunning("i"));
        Assert.Equal("a", snap.RunningId);

        engine.Start("i");
        engine.ResetAllToday();
        Assert.False(engine.GetSnapshot().IsAnyRunning);
        Assert.Empty(_sessions.Items);
    }

    [Fact]
    public void Tick_SplitsIndependentSessionAtLocalMidnight()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(2), "t", "t"));
        _time.SetUtcNow(new DateTimeOffset(2026, 3, 10, 21, 0, 0, TimeSpan.Zero)); // 23:00 local
        var engine = Create();
        engine.Start("a");
        engine.Start("i");
        _time.Advance(TimeSpan.FromHours(2));
        engine.Tick();

        var snap = engine.GetSnapshot();
        Assert.Equal(new DateOnly(2026, 3, 11), snap.Today);
        Assert.Equal(TimeSpan.FromHours(1), snap.GetCurrentValue("i"));
        Assert.Equal(TimeSpan.FromHours(1), snap.GetCurrentValue("a"));
        Assert.Equal(2, _sessions.GetOpen().Count);
        Assert.Equal(4, _sessions.Items.Count);
        var boundary = new DateTimeOffset(2026, 3, 10, 22, 0, 0, TimeSpan.Zero);
        Assert.Equal(2, _sessions.Items.Count(s => s.StopUtc == boundary));
    }

    [Fact]
    public void Load_KeepsOpenNormalAndOpenIndependent()
    {
        _sessions.InsertOpen("a", _time.GetUtcNow().AddMinutes(-10));
        _sessions.InsertOpen("i", _time.GetUtcNow().AddMinutes(-5));
        var engine = Create();

        var snap = engine.GetSnapshot();
        Assert.Equal("a", snap.RunningId);
        Assert.True(snap.IsRunning("i"));
        Assert.Equal(2, _sessions.GetOpen().Count);
    }

    [Fact]
    public void Load_SeveralOpenForOneIndependent_KeepsLatest()
    {
        var t = _time.GetUtcNow();
        _sessions.InsertOpen("i", t.AddMinutes(-10));
        _sessions.InsertOpen("i", t.AddMinutes(-4));
        Create();

        var open = Assert.Single(_sessions.GetOpen());
        Assert.Equal(t.AddMinutes(-4), open.StartUtc);
        Assert.Equal(t.AddMinutes(-4), _sessions.Items[0].StopUtc);
    }

    [Fact]
    public void ReloadDefinitions_IndependentBecomesNormal_KeepsMostRecentNormal()
    {
        var engine = Create();
        engine.Start("i");
        _time.Advance(TimeSpan.FromMinutes(1));
        engine.Start("a");
        _time.Advance(TimeSpan.FromMinutes(1));

        _defs.Update(_defs.Get("i")! with { IsIndependent = false });
        engine.ReloadDefinitions();

        var snap = engine.GetSnapshot();
        Assert.Equal("a", snap.RunningId);
        Assert.False(snap.IsRunning("i"));
        Assert.Single(_sessions.GetOpen());
    }

    [Fact]
    public void ReloadDefinitions_NormalBecomesIndependent_MovesToIndependentSet()
    {
        var engine = Create();
        engine.Start("b");

        _defs.Update(_defs.Get("b")! with { IsIndependent = true });
        engine.ReloadDefinitions();

        var snap = engine.GetSnapshot();
        Assert.Null(snap.RunningId);
        Assert.True(snap.IsRunning("b"));
        Assert.Single(_sessions.GetOpen());

        engine.Start("a");
        Assert.True(engine.GetSnapshot().IsRunning("b"));
    }

    [Fact]
    public void ReloadDefinitions_DeletedIndependent_Stops()
    {
        var engine = Create();
        engine.Start("i");
        _defs.SoftDelete("i");
        engine.ReloadDefinitions();
        Assert.False(engine.GetSnapshot().IsAnyRunning);
        Assert.Empty(_sessions.GetOpen());
    }

    [Fact]
    public void GrandTotal_IsUnionOfNormalAndIndependent()
    {
        var engine = Create();
        engine.Start("a");
        engine.Start("i");
        _time.Advance(TimeSpan.FromMinutes(10));

        var snap = engine.GetSnapshot();
        Assert.Equal(TimeSpan.FromMinutes(10), snap.GrandTotal); // fully overlapping: counted once
        Assert.Equal(TimeSpan.FromMinutes(10), snap.GetTodayTotal("i"));
        Assert.Equal(TimeSpan.FromMinutes(10), snap.GetTodayTotal("a"));

        engine.StopAll();
        _defs.SoftDelete("i");
        engine.ReloadDefinitions();
        Assert.Equal(TimeSpan.FromMinutes(10), engine.GetSnapshot().GrandTotal);
    }
}
