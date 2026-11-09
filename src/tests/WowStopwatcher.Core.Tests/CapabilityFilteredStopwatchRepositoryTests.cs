using Microsoft.Extensions.Time.Testing;

namespace WowStopwatcher.Core.Tests;

public class CapabilityFilteredStopwatchRepositoryTests
{
    private static readonly TimeSpan Goal = TimeSpan.FromHours(2);
    private static readonly DateTimeOffset Monday = new(2026, 3, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeStopwatchRepository _inner = new();

    private CapabilityFilteredStopwatchRepository Create(bool independent = true, bool goals = true, bool permanent = true) =>
        new(_inner, new StopwatchCapabilities(independent, goals, permanent));

    private void AddFull(string id = "a", int order = 0) =>
        _inner.Add(new StopwatchDefinition(id, id, "#111111", Goal, null, order, IsIndependent: true, IsPermanent: true));

    [Fact]
    public void Reads_IndependentOff_ClearsOnlyIndependent()
    {
        AddFull();
        var repo = Create(independent: false);

        foreach (var d in new[] { repo.GetAll().Single(), repo.Get("a")! })
        {
            Assert.False(d.IsIndependent);
            Assert.Equal(Goal, d.Goal);
            Assert.True(d.IsPermanent);
        }
    }

    [Fact]
    public void Reads_GoalsOff_ClearsOnlyGoal()
    {
        AddFull();
        var d = Create(goals: false).Get("a")!;

        Assert.Null(d.Goal);
        Assert.True(d.IsIndependent);
        Assert.True(d.IsPermanent);
    }

    [Fact]
    public void Reads_PermanentOff_ClearsOnlyPermanent()
    {
        AddFull();
        var d = Create(permanent: false).GetAll().Single();

        Assert.False(d.IsPermanent);
        Assert.True(d.IsIndependent);
        Assert.Equal(Goal, d.Goal);
    }

    [Fact]
    public void Get_Missing_ReturnsNull() => Assert.Null(Create(false, false, false).Get("x"));

    [Fact]
    public void Update_FromFilteredRead_KeepsStoredValuesOfDisabledCapabilities()
    {
        AddFull();
        var repo = Create(independent: false, goals: false, permanent: false);

        repo.Update(repo.Get("a")! with { Name = "Renamed" });

        var stored = _inner.Get("a")!;
        Assert.Equal("Renamed", stored.Name);
        Assert.True(stored.IsIndependent);
        Assert.Equal(Goal, stored.Goal);
        Assert.True(stored.IsPermanent);
    }

    [Fact]
    public void Update_EnabledCapabilities_AreWritten()
    {
        AddFull();
        var repo = Create(independent: false);

        repo.Update(repo.Get("a")! with { Goal = null, IsPermanent = false });

        var stored = _inner.Get("a")!;
        Assert.Null(stored.Goal);
        Assert.False(stored.IsPermanent);
        Assert.True(stored.IsIndependent);
    }

    [Fact]
    public void AllOn_PassesEverythingThrough()
    {
        AddFull("a", 0);
        var repo = Create();

        Assert.Equal(_inner.GetAll(), repo.GetAll());
        repo.Add(new StopwatchDefinition("b", "b", "#222222", Goal, null, 1, IsIndependent: true, IsPermanent: true));

        var b = _inner.Get("b")!;
        Assert.Equal(Goal, b.Goal);
        Assert.True(b.IsIndependent);
        Assert.True(b.IsPermanent);

        repo.Update(b with { Name = "B2", Goal = null, IsIndependent = false, IsPermanent = false });
        b = _inner.Get("b")!;
        Assert.Equal("B2", b.Name);
        Assert.Null(b.Goal);
        Assert.False(b.IsIndependent);
        Assert.False(b.IsPermanent);
    }

    [Fact]
    public void Add_StoresNeutralValuesForDisabledCapabilities()
    {
        var repo = Create(independent: false, goals: false, permanent: true);

        repo.Add(new StopwatchDefinition("c", "c", "#111111", Goal, null, 0, IsIndependent: true, IsPermanent: true));

        var stored = _inner.Get("c")!;
        Assert.False(stored.IsIndependent);
        Assert.Null(stored.Goal);
        Assert.True(stored.IsPermanent);
    }

    [Fact]
    public void SoftDelete_AndSaveSortOrder_PassThrough()
    {
        AddFull("a", 0);
        AddFull("b", 1);
        var repo = Create(false, false, false);

        repo.SaveSortOrder(["b", "a"]);
        repo.SoftDelete("a");

        Assert.Equal(0, _inner.Get("b")!.SortOrder);
        Assert.Equal(1, _inner.Get("a")!.SortOrder);
        Assert.True(_inner.Get("a")!.IsDeleted);
        Assert.True(_inner.Get("a")!.IsIndependent);
    }

    [Fact]
    public void Engine_IndependentOff_TreatsStoredIndependentAsNormal()
    {
        _inner.Add(new StopwatchDefinition("a", "A", "#111111", null, null, 0));
        _inner.Add(new StopwatchDefinition("i", "I", "#333333", null, null, 1, IsIndependent: true));
        var time = new FakeTimeProvider(Monday);
        var engine = new StopwatchEngine(Create(independent: false), new FakeSessionRepository(), time);

        engine.Start("a");
        engine.Start("i");

        var snap = engine.GetSnapshot();
        Assert.False(snap.IsRunning("a"));
        Assert.True(snap.IsRunning("i"));
        Assert.Equal("i", snap.RunningId);
    }

    [Fact]
    public void Engine_PermanentOff_ExpiresStoredPermanentStopwatch()
    {
        _inner.Add(new StopwatchDefinition("p", "P", "#111111", null, null, 0, IsPermanent: true));
        var sessions = new FakeSessionRepository();
        sessions.Close(sessions.InsertOpen("p", Monday), Monday.AddMinutes(5));
        var time = new FakeTimeProvider(Monday.AddDays(2));

        new StopwatchEngine(Create(permanent: false), sessions, time).GetSnapshot();

        Assert.True(_inner.Get("p")!.IsDeleted);
        Assert.True(_inner.Get("p")!.IsPermanent);
    }

    [Fact]
    public void Engine_PermanentOn_KeepsStoredPermanentStopwatch()
    {
        _inner.Add(new StopwatchDefinition("p", "P", "#111111", null, null, 0, IsPermanent: true));
        var sessions = new FakeSessionRepository();
        sessions.Close(sessions.InsertOpen("p", Monday), Monday.AddMinutes(5));
        var time = new FakeTimeProvider(Monday.AddDays(2));

        new StopwatchEngine(Create(), sessions, time).GetSnapshot();

        Assert.False(_inner.Get("p")!.IsDeleted);
    }
}
