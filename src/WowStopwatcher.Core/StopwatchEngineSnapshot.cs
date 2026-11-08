namespace WowStopwatcher.Core;

/// <summary>
/// An immutable, consistent view of the engine at one instant (<see cref="NowUtc"/>).
/// The running session is measured up to <see cref="NowUtc"/>.
/// </summary>
public sealed class StopwatchEngineSnapshot
{
    public StopwatchEngineSnapshot(
        DateTimeOffset nowUtc,
        DateOnly today,
        IReadOnlyList<StopwatchDefinition> allStopwatches,
        IReadOnlyList<Session> todaySessions,
        IReadOnlyList<Session> runningSessions,
        string? lastUsedId)
    {
        NowUtc = nowUtc;
        Today = today;
        AllStopwatches = allStopwatches;
        Stopwatches = allStopwatches.Where(s => !s.IsDeleted).ToArray();
        TodaySessions = todaySessions;
        RunningSessions = runningSessions;
        var independentIds = allStopwatches.Where(s => s.IsIndependent).Select(s => s.Id).ToHashSet();
        RunningSession = runningSessions.FirstOrDefault(s => !independentIds.Contains(s.StopwatchId));
        LastUsedId = lastUsedId;
        GrandTotal = SessionSlicer.Total(SessionSlicer.Build(todaySessions, allStopwatches, nowUtc));
    }

    /// <summary>The instant this snapshot was taken (UTC, millisecond precision).</summary>
    public DateTimeOffset NowUtc { get; }

    /// <summary>Today's local date.</summary>
    public DateOnly Today { get; }

    /// <summary>Non-deleted stopwatches ordered by SortOrder.</summary>
    public IReadOnlyList<StopwatchDefinition> Stopwatches { get; }

    /// <summary>All stopwatches including deleted ones, ordered by SortOrder.</summary>
    public IReadOnlyList<StopwatchDefinition> AllStopwatches { get; }

    /// <summary>Today's sessions of all stopwatches in chronological order; the running one has StopUtc == null.</summary>
    public IReadOnlyList<Session> TodaySessions { get; }

    /// <summary>All open sessions: the running normal stopwatch (if any) and every running independent one.</summary>
    public IReadOnlyList<Session> RunningSessions { get; }

    /// <summary>True when any stopwatch, normal or independent, is running.</summary>
    public bool IsAnyRunning => RunningSessions.Count > 0;

    /// <summary>The open session of the running normal (non-independent) stopwatch, or null when none runs.</summary>
    public Session? RunningSession { get; }

    /// <summary>Id of the running normal stopwatch, or null.</summary>
    public string? RunningId => RunningSession?.StopwatchId;

    /// <summary>Elapsed time of the running normal session (zero when none runs).</summary>
    public TimeSpan RunningElapsed => RunningSession?.GetDuration(NowUtc) ?? TimeSpan.Zero;

    /// <summary>The normal (non-independent) stopwatch that ran most recently (running or stopped), or null if none ever ran.</summary>
    public string? LastUsedId { get; }

    /// <summary>Wall-clock time today during which any stopwatch (normal or independent) ran; overlapping time counts once.</summary>
    public TimeSpan GrandTotal { get; }

    /// <summary>The stopwatch with <paramref name="id"/> (including deleted), or null.</summary>
    public StopwatchDefinition? Find(string id) => AllStopwatches.FirstOrDefault(s => s.Id == id);

    /// <summary>True when the stopwatch with <paramref name="id"/> is running (normal or independent).</summary>
    public bool IsRunning(string id) => RunningSessions.Any(s => s.StopwatchId == id);

    /// <summary>
    /// True when the stopwatch is already running, or starting it would not exceed
    /// <see cref="StopwatchEngine.MaxConcurrent"/> (a normal start replaces the running normal one).
    /// False for unknown or deleted stopwatches.
    /// </summary>
    public bool CanStart(string id)
    {
        if (IsRunning(id))
        {
            return true;
        }

        if (Find(id) is not { IsDeleted: false } def)
        {
            return false;
        }

        return RunningSessions.Count < StopwatchEngine.MaxConcurrent || (!def.IsIndependent && RunningSession is not null);
    }

    /// <summary>Duration of <paramref name="session"/> at <see cref="NowUtc"/>.</summary>
    public TimeSpan GetDuration(Session session) => session.GetDuration(NowUtc);

    /// <summary>Today's sessions of one stopwatch in chronological order (including the running one).</summary>
    public IReadOnlyList<Session> GetTodaySessions(string id) =>
        TodaySessions.Where(s => s.StopwatchId == id).ToArray();

    /// <summary>Total of today's sessions of one stopwatch, including the running one.</summary>
    public TimeSpan GetTodayTotal(string id) => Sum(TodaySessions.Where(s => s.StopwatchId == id));

    /// <summary>
    /// The value shown in a stopwatch row: the running session's elapsed time, else the last finished
    /// session of today, else zero.
    /// </summary>
    public TimeSpan GetCurrentValue(string id)
    {
        if (RunningSessions.FirstOrDefault(s => s.StopwatchId == id) is { } running)
        {
            return running.GetDuration(NowUtc);
        }

        var last = TodaySessions.LastOrDefault(s => s.StopwatchId == id && !s.IsOpen);
        return last?.GetDuration(NowUtc) ?? TimeSpan.Zero;
    }

    private TimeSpan Sum(IEnumerable<Session> sessions) =>
        sessions.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.GetDuration(NowUtc));
}
