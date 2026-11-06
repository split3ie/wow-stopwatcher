namespace WowStopwatcher.Core;

/// <summary>
/// Runs the stopwatches: at most one runs at a time, every run is a persisted session.
/// Thread-safe (a single lock), so a UI thread and a hotkey callback can both call it.
/// The <see cref="Changed"/> event is raised after the lock is released, on the calling thread.
/// </summary>
public sealed class StopwatchEngine
{
    private readonly IStopwatchRepository _stopwatches;
    private readonly ISessionRepository _sessions;
    private readonly TimeProvider _time;
    private readonly object _lock = new();

    private IReadOnlyList<StopwatchDefinition> _definitions = [];
    private IReadOnlyList<Session> _todaySessions = [];
    private DateOnly _today;
    private Session? _running;
    private string? _lastUsedId;

    public StopwatchEngine(IStopwatchRepository stopwatches, ISessionRepository sessions, TimeProvider time)
    {
        _stopwatches = stopwatches;
        _sessions = sessions;
        _time = time;
        Load();
    }

    /// <summary>Raised after any state change (start, stop, reset, midnight split, reload).</summary>
    public event EventHandler? Changed;

    /// <summary>A consistent view of the engine at the current time. Cheap; call it on every UI tick.</summary>
    public StopwatchEngineSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return BuildSnapshot(Now());
        }
    }

    /// <summary>Starts the stopwatch (stopping the running one). Does nothing if it is unknown, deleted or already running.</summary>
    public void Start(string id) => Mutate(now =>
    {
        if (!IsStartable(id) || _running?.StopwatchId == id)
        {
            return false;
        }

        StopRunning(now);
        StartNew(id, now);
        return true;
    });

    /// <summary>Stops the stopwatch if it is running, otherwise starts it (stopping any other running one).</summary>
    public void Toggle(string id) => Mutate(now =>
    {
        if (_running?.StopwatchId == id)
        {
            StopRunning(now);
            return true;
        }

        if (!IsStartable(id))
        {
            return false;
        }

        StopRunning(now);
        StartNew(id, now);
        return true;
    });

    /// <summary>Stops the running stopwatch, if any.</summary>
    public void StopAll() => Mutate(now => StopRunning(now));

    /// <summary>
    /// If something runs, stops it and starts the next non-deleted stopwatch by SortOrder (wrapping around).
    /// If nothing runs, starts the last used stopwatch, else the first.
    /// </summary>
    public void StartNext() => Mutate(now =>
    {
        var active = _definitions.Where(d => !d.IsDeleted).ToArray();
        if (active.Length == 0)
        {
            return false;
        }

        string target;
        if (_running is { } running)
        {
            var index = Array.FindIndex(active, d => d.Id == running.StopwatchId);
            target = active[(index + 1) % active.Length].Id;
        }
        else
        {
            target = _lastUsedId is { } last && active.Any(d => d.Id == last) ? last : active[0].Id;
        }

        StopRunning(now);
        StartNew(target, now);
        return true;
    });

    /// <summary>Deletes today's sessions of the stopwatch (stopping it if running).</summary>
    public void ResetToday(string id) => Mutate(now =>
    {
        var (from, to) = LocalDays.DayRangeUtc(_today, _time.LocalTimeZone);
        _sessions.DeleteForStopwatchInRange(id, from, to);
        if (_running?.StopwatchId == id)
        {
            _running = null;
        }

        _lastUsedId = _sessions.GetLatest()?.StopwatchId;
        return true;
    });

    /// <summary>Deletes all of today's sessions and stops the running stopwatch.</summary>
    public void ResetAllToday() => Mutate(now =>
    {
        _sessions.DeleteForLocalDays([_today], _time.LocalTimeZone);
        _running = null;
        _lastUsedId = _sessions.GetLatest()?.StopwatchId;
        return true;
    });

    /// <summary>
    /// Call every ~50 ms to 1 s. Splits a running session that crossed local midnight and
    /// switches "today" when the date changed. Raises <see cref="Changed"/> only if something changed.
    /// </summary>
    public void Tick() => Mutate(now => SplitAtMidnight(now));

    /// <summary>Re-reads the definitions after a Settings change. Stops the running stopwatch if it was deleted.</summary>
    public void ReloadDefinitions() => Mutate(now =>
    {
        _definitions = _stopwatches.GetAll(includeDeleted: true);
        if (_running is { } running && !IsStartable(running.StopwatchId))
        {
            StopRunning(now);
        }

        return true;
    });

    private void Load()
    {
        lock (_lock)
        {
            var now = Now();
            _definitions = _stopwatches.GetAll(includeDeleted: true);
            _lastUsedId = _sessions.GetLatest()?.StopwatchId;

            // More than one open session should not happen; keep the latest and close the others where the next began.
            var open = _sessions.GetOpen();
            for (var i = 0; i < open.Count - 1; i++)
            {
                _sessions.Close(open[i].Id, open[i + 1].StartUtc);
            }

            _running = open.Count > 0 ? open[^1] : null;
            if (_running is { } running && !IsStartable(running.StopwatchId))
            {
                StopRunning(now);
            }

            _today = LocalDays.ToLocalDate(now, _time.LocalTimeZone);
            SplitAtMidnight(now);
            RefreshToday();
        }
    }

    private void Mutate(Func<DateTimeOffset, bool> action)
    {
        bool changed;
        lock (_lock)
        {
            var now = Now();
            changed = SplitAtMidnight(now);
            changed |= action(now);
            if (changed)
            {
                RefreshToday();
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    // Closes a running session at each local midnight it crossed and reopens it at that midnight.
    private bool SplitAtMidnight(DateTimeOffset now)
    {
        var zone = _time.LocalTimeZone;
        var today = LocalDays.ToLocalDate(now, zone);
        var changed = today != _today;
        _today = today;

        while (_running is { } running && LocalDays.ToLocalDate(running.StartUtc, zone) < today)
        {
            var boundary = LocalDays.StartOfDayUtc(LocalDays.ToLocalDate(running.StartUtc, zone).AddDays(1), zone);
            if (boundary <= running.StartUtc || boundary > now)
            {
                break;
            }

            _sessions.Close(running.Id, boundary);
            var id = _sessions.InsertOpen(running.StopwatchId, boundary);
            _running = new Session(id, running.StopwatchId, boundary, null);
            changed = true;
        }

        return changed;
    }

    private bool StopRunning(DateTimeOffset now)
    {
        if (_running is not { } running)
        {
            return false;
        }

        _sessions.Close(running.Id, now < running.StartUtc ? running.StartUtc : now);
        _running = null;
        return true;
    }

    private void StartNew(string id, DateTimeOffset now)
    {
        var sessionId = _sessions.InsertOpen(id, now);
        _running = new Session(sessionId, id, now, null);
        _lastUsedId = id;
    }

    private bool IsStartable(string id) => _definitions.Any(d => d.Id == id && !d.IsDeleted);

    private void RefreshToday()
    {
        var (from, to) = LocalDays.DayRangeUtc(_today, _time.LocalTimeZone);
        _todaySessions = _sessions.GetInRange(from, to);
        if (_running is { } running && _todaySessions.All(s => s.Id != running.Id))
        {
            _todaySessions = [.. _todaySessions, running];
        }
    }

    private StopwatchEngineSnapshot BuildSnapshot(DateTimeOffset now)
    {
        var today = LocalDays.ToLocalDate(now, _time.LocalTimeZone);
        if (today != _today)
        {
            // The date rolled over since the last Tick; show the new day without writing anything.
            var (from, to) = LocalDays.DayRangeUtc(today, _time.LocalTimeZone);
            var sessions = _sessions.GetInRange(from, to);
            return new StopwatchEngineSnapshot(now, today, _definitions, sessions, _running, _lastUsedId);
        }

        return new StopwatchEngineSnapshot(now, _today, _definitions, _todaySessions, _running, _lastUsedId);
    }

    // Millisecond precision, matching what the database stores.
    private DateTimeOffset Now() => DateTimeOffset.FromUnixTimeMilliseconds(_time.GetUtcNow().ToUnixTimeMilliseconds());
}
