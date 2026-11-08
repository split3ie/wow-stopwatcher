namespace WowStopwatcher.Core;

/// <summary>
/// Runs the stopwatches: at most one normal stopwatch runs at a time, plus any number of independent ones; every run is a persisted session.
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
    private readonly Dictionary<string, Session> _independent = [];
    private string? _lastUsedId;

    /// <summary>Most stopwatches (normal and independent together) that may run at the same time.</summary>
    public const int MaxConcurrent = 3;

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

    /// <summary>
    /// Starts the stopwatch. A normal stopwatch stops the running normal one; an independent stopwatch
    /// touches no other. Does nothing if it is unknown, deleted or already running.
    /// </summary>
    public void Start(string id) => Mutate(now =>
    {
        if (!IsStartable(id) || IsRunningId(id) || !HasRoomFor(id))
        {
            return false;
        }

        StartStopwatch(id, now);
        return true;
    });

    /// <summary>Stops the stopwatch if it is running, otherwise starts it (see <see cref="Start"/>).</summary>
    public void Toggle(string id) => Mutate(now =>
    {
        if (_running?.StopwatchId == id)
        {
            StopRunning(now);
            return true;
        }

        if (_independent.ContainsKey(id))
        {
            StopIndependent(id, now);
            return true;
        }

        if (!IsStartable(id) || !HasRoomFor(id))
        {
            return false;
        }

        StartStopwatch(id, now);
        return true;
    });

    /// <summary>Stops the running normal stopwatch and all running independent stopwatches.</summary>
    public void StopAll() => Mutate(now => StopEverything(now));

    /// <summary>
    /// Cycles over the non-deleted, non-independent stopwatches only; independent ones are untouched.
    /// If a normal one runs, stops it and starts the next by SortOrder (wrapping around).
    /// If none runs, starts the last used normal stopwatch, else the first. Does nothing without normal stopwatches.
    /// </summary>
    public void StartNext() => Mutate(now =>
    {
        var active = _definitions.Where(d => !d.IsDeleted && !d.IsIndependent).ToArray();
        if (active.Length == 0)
        {
            return false;
        }

        if (_running is null && OpenCount() >= MaxConcurrent)
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

        _independent.Remove(id);
        RefreshLastUsed();
        return true;
    });

    /// <summary>Deletes all of today's sessions and stops every running stopwatch.</summary>
    public void ResetAllToday() => Mutate(now =>
    {
        _sessions.DeleteForLocalDays([_today], _time.LocalTimeZone);
        _running = null;
        _independent.Clear();
        RefreshLastUsed();
        return true;
    });

    /// <summary>
    /// Call every ~50 ms to 1 s. Splits running sessions that crossed local midnight and
    /// switches "today" when the date changed. Raises <see cref="Changed"/> only if something changed.
    /// </summary>
    public void Tick() => Mutate(now => SplitAtMidnight(now));

    /// <summary>
    /// Re-reads the definitions after a Settings change. Stops deleted stopwatches, re-sorts open sessions into
    /// normal and independent by the current flags, and keeps only the most recently started normal one.
    /// </summary>
    public void ReloadDefinitions() => Mutate(now =>
    {
        _definitions = _stopwatches.GetAll(includeDeleted: true);
        var open = AllOpen().ToList();
        _running = null;
        _independent.Clear();

        var normal = new List<Session>();
        foreach (var session in open)
        {
            if (!IsStartable(session.StopwatchId))
            {
                CloseAt(session, now);
            }
            else if (IsIndependent(session.StopwatchId))
            {
                _independent[session.StopwatchId] = session;
            }
            else
            {
                normal.Add(session);
            }
        }

        foreach (var session in normal.OrderBy(s => s.StartUtc).ThenBy(s => s.Id))
        {
            if (_running is { } previous)
            {
                CloseAt(previous, now);
            }

            _running = session;
        }

        EnforceLimit(now);
        RefreshLastUsed();
        return true;
    });

    private void Load()
    {
        lock (_lock)
        {
            var now = Now();
            _definitions = _stopwatches.GetAll(includeDeleted: true);

            // More than one open session per slot should not happen; keep the latest and close the others where the next began.
            var open = _sessions.GetOpen().OrderBy(s => s.StartUtc).ThenBy(s => s.Id).ToList();
            _running = KeepLatest(open.Where(s => !IsIndependent(s.StopwatchId)).ToList());
            foreach (var group in open.Where(s => IsIndependent(s.StopwatchId)).GroupBy(s => s.StopwatchId))
            {
                _independent[group.Key] = KeepLatest(group.ToList())!;
            }

            if (_running is { } running && !IsStartable(running.StopwatchId))
            {
                StopRunning(now);
            }

            foreach (var session in _independent.Values.Where(s => !IsStartable(s.StopwatchId)).ToList())
            {
                StopIndependent(session.StopwatchId, now);
            }

            EnforceLimit(now);
            RefreshLastUsed();
            _today = LocalDays.ToLocalDate(now, _time.LocalTimeZone);
            SplitAtMidnight(now);
            RefreshToday();
        }
    }

    // Sessions are ordered by start. Closes every one but the last at the start of the next and returns the last.
    private Session? KeepLatest(List<Session> ordered)
    {
        for (var i = 0; i < ordered.Count - 1; i++)
        {
            _sessions.Close(ordered[i].Id, ordered[i + 1].StartUtc);
        }

        return ordered.Count > 0 ? ordered[^1] : null;
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

    // Closes each open session at every local midnight it crossed and reopens it at that midnight.
    private bool SplitAtMidnight(DateTimeOffset now)
    {
        var zone = _time.LocalTimeZone;
        var today = LocalDays.ToLocalDate(now, zone);
        var changed = today != _today;
        _today = today;

        if (_running is { } running)
        {
            var split = SplitSession(running, now, today, zone);
            if (split != running)
            {
                _running = split;
                changed = true;
            }
        }

        foreach (var (id, session) in _independent.ToArray())
        {
            var split = SplitSession(session, now, today, zone);
            if (split != session)
            {
                _independent[id] = split;
                changed = true;
            }
        }

        return changed;
    }

    private Session SplitSession(Session session, DateTimeOffset now, DateOnly today, TimeZoneInfo zone)
    {
        while (LocalDays.ToLocalDate(session.StartUtc, zone) < today)
        {
            var boundary = LocalDays.StartOfDayUtc(LocalDays.ToLocalDate(session.StartUtc, zone).AddDays(1), zone);
            if (boundary <= session.StartUtc || boundary > now)
            {
                break;
            }

            _sessions.Close(session.Id, boundary);
            var id = _sessions.InsertOpen(session.StopwatchId, boundary);
            session = new Session(id, session.StopwatchId, boundary, null);
        }

        return session;
    }

    private bool StopRunning(DateTimeOffset now)
    {
        if (_running is not { } running)
        {
            return false;
        }

        CloseAt(running, now);
        _running = null;
        return true;
    }

    private void StopIndependent(string id, DateTimeOffset now)
    {
        if (_independent.Remove(id, out var session))
        {
            CloseAt(session, now);
        }
    }

    private bool StopEverything(DateTimeOffset now)
    {
        var changed = StopRunning(now);
        foreach (var id in _independent.Keys.ToArray())
        {
            StopIndependent(id, now);
            changed = true;
        }

        return changed;
    }

    private void CloseAt(Session session, DateTimeOffset now) =>
        _sessions.Close(session.Id, now < session.StartUtc ? session.StartUtc : now);

    private void StartStopwatch(string id, DateTimeOffset now)
    {
        if (IsIndependent(id))
        {
            _independent[id] = new Session(_sessions.InsertOpen(id, now), id, now, null);
            return;
        }

        StopRunning(now);
        StartNew(id, now);
    }

    private void StartNew(string id, DateTimeOffset now)
    {
        var sessionId = _sessions.InsertOpen(id, now);
        _running = new Session(sessionId, id, now, null);
        _lastUsedId = id;
    }

    private int OpenCount() => (_running is null ? 0 : 1) + _independent.Count;

    // A normal start replaces the running normal one, so it needs no free slot then.
    private bool HasRoomFor(string id) => OpenCount() < MaxConcurrent || (_running is not null && !IsIndependent(id));

    // Should not happen; if more than MaxConcurrent sessions are open, keeps the most recently started ones.
    private void EnforceLimit(DateTimeOffset now)
    {
        foreach (var session in AllOpen().OrderByDescending(o => o.StartUtc).ThenByDescending(o => o.Id).Skip(MaxConcurrent).ToList())
        {
            if (_running?.Id == session.Id)
            {
                _running = null;
                CloseAt(session, now);
            }
            else
            {
                StopIndependent(session.StopwatchId, now);
            }
        }
    }

    private bool IsIndependent(string id) => _definitions.Any(d => d.Id == id && d.IsIndependent);

    private bool IsRunningId(string id) => _running?.StopwatchId == id || _independent.ContainsKey(id);

    private IEnumerable<Session> AllOpen() =>
        (_running is { } running ? [running] : Enumerable.Empty<Session>()).Concat(_independent.Values);

    // The most recently used NORMAL stopwatch (independent ones are ignored).
    private void RefreshLastUsed()
    {
        var normalIds = _definitions.Where(d => !d.IsIndependent).Select(d => d.Id).ToArray();
        _lastUsedId = _sessions.GetLatestForStopwatches(normalIds)?.StopwatchId;
    }

    private bool IsStartable(string id) => _definitions.Any(d => d.Id == id && !d.IsDeleted);

    private void RefreshToday()
    {
        var (from, to) = LocalDays.DayRangeUtc(_today, _time.LocalTimeZone);
        _todaySessions = _sessions.GetInRange(from, to);
        foreach (var open in AllOpen())
        {
            if (_todaySessions.All(s => s.Id != open.Id))
            {
                _todaySessions = [.. _todaySessions, open];
            }
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
            return new StopwatchEngineSnapshot(now, today, _definitions, sessions, AllOpen().OrderBy(o => o.StartUtc).ToArray(), _lastUsedId);
        }

        return new StopwatchEngineSnapshot(now, _today, _definitions, _todaySessions, AllOpen().OrderBy(o => o.StartUtc).ToArray(), _lastUsedId);
    }

    // Millisecond precision, matching what the database stores.
    private DateTimeOffset Now() => DateTimeOffset.FromUnixTimeMilliseconds(_time.GetUtcNow().ToUnixTimeMilliseconds());
}
