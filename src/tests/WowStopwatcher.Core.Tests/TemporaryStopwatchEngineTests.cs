using Microsoft.Extensions.Time.Testing;

namespace WowStopwatcher.Core.Tests;

public class TemporaryStopwatchEngineTests
{
    private readonly FakeStopwatchRepository _defs = new();
    private readonly FakeSessionRepository _sessions = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero)); // Tuesday

    private static readonly DateTimeOffset Monday = new(2026, 3, 9, 12, 0, 0, TimeSpan.Zero);

    private StopwatchEngine Create() => new(_defs, _sessions, _time);

    private StopwatchEngine CreateOnWednesday()
    {
        _time.Advance(TimeSpan.FromDays(1));
        return Create();
    }

    private void AddUsed(string id, int order, DateTimeOffset usedAt, bool permanent = false, string? shortcut = null)
    {
        _defs.Add(new StopwatchDefinition(id, id, "#111111", null, shortcut, order, IsPermanent: permanent));
        var sessionId = _sessions.InsertOpen(id, usedAt);
        _sessions.Close(sessionId, usedAt.AddMinutes(5));
    }

    [Fact]
    public void Load_HidesExpiredTemporaryAndKeepsSessions()
    {
        AddUsed("old", 0, Monday);
        AddUsed("perm", 1, Monday, permanent: true);
        AddUsed("fresh", 2, Monday.AddDays(1));

        var snapshot = CreateOnWednesday().GetSnapshot();

        Assert.True(_defs.Get("old")!.IsDeleted);
        Assert.False(_defs.Get("perm")!.IsDeleted);
        Assert.False(_defs.Get("fresh")!.IsDeleted);
        Assert.Contains(snapshot.AllStopwatches, d => d.Id == "old" && d.IsDeleted);
        Assert.Single(_sessions.Items, s => s.StopwatchId == "old");
    }

    [Fact]
    public void Tick_AcrossMidnight_HidesExpiredAndRaisesChanged()
    {
        AddUsed("a", 0, Monday);
        var engine = Create();
        Assert.False(_defs.Get("a")!.IsDeleted);
        var changed = 0;
        engine.Changed += (_, _) => changed++;

        _time.Advance(TimeSpan.FromHours(13));
        engine.Tick();

        Assert.True(_defs.Get("a")!.IsDeleted);
        Assert.Equal(1, changed);
        Assert.True(engine.GetSnapshot().AllStopwatches.Single(d => d.Id == "a").IsDeleted);
    }

    [Fact]
    public void RunningStopwatch_IsNotHidden()
    {
        _defs.Add(new StopwatchDefinition("a", "A", "#111111", null, null, 0, ActiveSinceUtc: Monday));
        var engine = Create();
        engine.Start("a");

        _time.Advance(TimeSpan.FromDays(3));
        engine.Tick();

        Assert.False(_defs.Get("a")!.IsDeleted);
        Assert.Equal("a", engine.GetSnapshot().RunningId);
    }

    [Fact]
    public void Restore_UnDeletesSetsActiveSinceAndMovesLast()
    {
        AddUsed("old", 0, Monday);
        AddUsed("b", 1, Monday.AddDays(2));
        AddUsed("c", 2, Monday.AddDays(2));
        var engine = CreateOnWednesday();
        Assert.True(_defs.Get("old")!.IsDeleted);
        var changed = 0;
        engine.Changed += (_, _) => changed++;

        Assert.True(engine.Restore("old"));

        var old = _defs.Get("old")!;
        Assert.False(old.IsDeleted);
        Assert.False(old.IsPermanent);
        Assert.Equal(_time.GetUtcNow(), old.ActiveSinceUtc);
        Assert.Equal(["b", "c", "old"], _defs.GetAll().Select(d => d.Id));
        Assert.Equal(1, changed);
        Assert.Contains(engine.GetSnapshot().AllStopwatches, d => d.Id == "old" && !d.IsDeleted);

        engine.Tick();
        Assert.False(_defs.Get("old")!.IsDeleted);
    }

    [Fact]
    public void Restore_KeepsDeletedAfterVisibleWithUniqueSortOrders()
    {
        AddUsed("v", 0, Monday.AddDays(2));
        AddUsed("d1", 1, Monday);
        AddUsed("d2", 2, Monday);
        var engine = CreateOnWednesday();

        Assert.True(engine.Restore("d2"));

        var all = _defs.GetAll(includeDeleted: true);
        Assert.Equal(["v", "d2", "d1"], all.Select(d => d.Id));
        Assert.Equal([0, 1, 2], all.Select(d => d.SortOrder));
    }

    [Fact]
    public void Restore_KeepsShortcutWhenFree()
    {
        AddUsed("old", 0, Monday, shortcut: "Ctrl+F5");
        var engine = CreateOnWednesday();

        Assert.True(engine.Restore("old"));

        Assert.Equal("Ctrl+F5", _defs.Get("old")!.Shortcut);
    }

    [Fact]
    public void Restore_ShortcutConflict_GetsNextDefault()
    {
        AddUsed("old", 0, Monday, shortcut: "Ctrl+F1");
        AddUsed("b", 1, Monday.AddDays(2), shortcut: "ctrl+f1");
        AddUsed("c", 2, Monday.AddDays(2), shortcut: "Ctrl+F2");
        var engine = CreateOnWednesday();

        Assert.True(engine.Restore("old"));

        Assert.Equal("Ctrl+F3", _defs.Get("old")!.Shortcut);
    }

    [Fact]
    public void Restore_UnknownOrVisible_ReturnsFalse()
    {
        AddUsed("b", 0, Monday.AddDays(2));
        var engine = CreateOnWednesday();
        var changed = 0;
        engine.Changed += (_, _) => changed++;

        Assert.False(engine.Restore("missing"));
        Assert.False(engine.Restore("b"));
        Assert.Equal(0, changed);
    }
}
