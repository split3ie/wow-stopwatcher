using Microsoft.Extensions.Time.Testing;

namespace WowStopwatcher.Core.Tests;

public class StopwatchInterruptionTests
{
    private sealed class FakeLastAliveStore : ILastAliveStore
    {
        public DateTimeOffset? Value { get; set; }

        public int Writes { get; private set; }

        public DateTimeOffset? Read() => Value;

        public void Write(DateTimeOffset utc)
        {
            Value = utc;
            Writes++;
        }
    }

    private sealed class FakeSettings : ISettingsRepository
    {
        public Dictionary<string, string> Items { get; } = [];

        public string? Get(string key) => Items.GetValueOrDefault(key);

        public void Set(string key, string value) => Items[key] = value;
    }

    private readonly FakeStopwatchRepository _defs = new();
    private readonly FakeSessionRepository _sessions = new();
    private readonly FakeLastAliveStore _store = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

    public StopwatchInterruptionTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _defs.Add(new StopwatchDefinition("a", "A", "#111111", null, null, 0));
        _defs.Add(new StopwatchDefinition("i", "I", "#333333", null, null, 1, IsIndependent: true));
    }

    private StopwatchEngine Create() => new(_defs, _sessions, _time, new TemporaryStopwatchExpiryPolicy(), _store);

    [Fact]
    public void Gap_DuringRun_StopsAtLastAlive_AndInterruptionIsTakenOnce()
    {
        var engine = Create();
        engine.Start("a");
        var aliveAt = _time.GetUtcNow().AddMinutes(1);
        _time.Advance(TimeSpan.FromMinutes(1));
        engine.Tick();
        _time.Advance(TimeSpan.FromHours(3));
        engine.Tick();

        Assert.Empty(_sessions.GetOpen());
        Assert.Equal(aliveAt, _sessions.Items.Single().StopUtc);
        var interruption = engine.TakeInterruption();
        Assert.NotNull(interruption);
        Assert.Equal(aliveAt, interruption.StoppedAtUtc);
        Assert.Equal(["a"], interruption.StopwatchIds);
        Assert.Null(engine.TakeInterruption());
        Assert.False(engine.GetSnapshot().IsAnyRunning);
    }

    [Fact]
    public void Gap_BelowThreshold_StopsNothing()
    {
        var engine = Create();
        engine.Start("a");
        _time.Advance(TimeSpan.FromSeconds(100));
        engine.Tick();

        Assert.Single(_sessions.GetOpen());
        Assert.Null(engine.TakeInterruption());
    }

    [Fact]
    public void Gap_WithNothingRunning_HasNoInterruption()
    {
        var engine = Create();
        _time.Advance(TimeSpan.FromHours(3));
        engine.Tick();

        Assert.Null(engine.TakeInterruption());
    }

    private sealed class SettableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public void ClockMovedBackwards_StopsNothing()
    {
        var clock = new SettableTimeProvider(_time.GetUtcNow());
        var engine = new StopwatchEngine(_defs, _sessions, clock, new TemporaryStopwatchExpiryPolicy(), _store);
        engine.Start("a");
        clock.Now = clock.Now.AddHours(-5);
        engine.Tick();

        Assert.Single(_sessions.GetOpen());
        Assert.Null(engine.TakeInterruption());
    }

    [Fact]
    public void NormalAndIndependent_AreBothStopped()
    {
        var engine = Create();
        engine.Start("a");
        engine.Start("i");
        var aliveAt = _time.GetUtcNow();
        _time.Advance(TimeSpan.FromHours(1));
        engine.Tick();

        Assert.Empty(_sessions.GetOpen());
        Assert.All(_sessions.Items, s => Assert.Equal(aliveAt, s.StopUtc));
        Assert.Equal(["a", "i"], engine.TakeInterruption()!.StopwatchIds);
    }

    [Fact]
    public void Startup_WithStaleLastAlive_StopsAtThatTime_EvenAcrossMidnight()
    {
        var start = new DateTimeOffset(2026, 3, 10, 23, 0, 0, TimeSpan.Zero);
        var aliveAt = new DateTimeOffset(2026, 3, 10, 23, 30, 0, TimeSpan.Zero);
        _sessions.Items.Add(new Session(1, "a", start, null));
        _store.Value = aliveAt;
        _time.SetUtcNow(new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero));

        var engine = Create();

        var session = Assert.Single(_sessions.Items);
        Assert.Equal(aliveAt, session.StopUtc);
        Assert.Equal(aliveAt, engine.TakeInterruption()!.StoppedAtUtc);
        Assert.False(engine.GetSnapshot().IsAnyRunning);
    }

    [Fact]
    public void Startup_WithFreshLastAlive_Resumes()
    {
        _sessions.Items.Add(new Session(1, "a", _time.GetUtcNow().AddMinutes(-10), null));
        _store.Value = _time.GetUtcNow().AddSeconds(-30);

        var engine = Create();

        Assert.True(engine.GetSnapshot().IsRunning("a"));
        Assert.Null(engine.TakeInterruption());
    }

    [Fact]
    public void Startup_WithoutStoredLastAlive_Resumes()
    {
        _sessions.Items.Add(new Session(1, "a", _time.GetUtcNow().AddHours(-10), null));

        var engine = Create();

        Assert.True(engine.GetSnapshot().IsRunning("a"));
        Assert.Null(engine.TakeInterruption());
    }

    [Fact]
    public void Heartbeat_IsThrottled_AndWrittenOnStateChange()
    {
        var engine = Create();
        var afterLoad = _store.Writes;
        engine.Start("a");
        Assert.Equal(afterLoad + 1, _store.Writes);

        for (var i = 0; i < 5; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(5));
            engine.Tick();
        }

        Assert.Equal(afterLoad + 1, _store.Writes);

        _time.Advance(StopwatchEngine.HeartbeatInterval);
        engine.Tick();
        Assert.Equal(afterLoad + 2, _store.Writes);
        Assert.Equal(_time.GetUtcNow(), _store.Value);
    }

    [Fact]
    public void SettingsStore_RoundTrips_AndIgnoresGarbage()
    {
        var settings = new FakeSettings();
        var store = new SettingsLastAliveStore(settings);
        Assert.Null(store.Read());

        var value = new DateTimeOffset(2026, 3, 10, 12, 0, 0, 123, TimeSpan.Zero);
        store.Write(value);
        Assert.Equal(value, store.Read());

        settings.Items[SettingsLastAliveStore.Key] = "nonsense";
        Assert.Null(store.Read());
    }
}
