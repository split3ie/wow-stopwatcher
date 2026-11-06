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
        Session? runningSession,
        string? lastUsedId)
    {
        NowUtc = nowUtc;
        Today = today;
        AllStopwatches = allStopwatches;
        Stopwatches = allStopwatches.Where(s => !s.IsDeleted).ToArray();
        TodaySessions = todaySessions;
        RunningSession = runningSession;
        LastUsedId = lastUsedId;
        GrandTotal = Sum(todaySessions);
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

    /// <summary>The open session of the running stopwatch, or null when nothing runs.</summary>
    public Session? RunningSession { get; }

    /// <summary>Id of the running stopwatch, or null.</summary>
    public string? RunningId => RunningSession?.StopwatchId;

    /// <summary>Elapsed time of the running session (zero when nothing runs).</summary>
    public TimeSpan RunningElapsed => RunningSession?.GetDuration(NowUtc) ?? TimeSpan.Zero;

    /// <summary>The stopwatch that ran most recently (running or stopped), or null if none ever ran.</summary>
    public string? LastUsedId { get; }

    /// <summary>Sum of all today's sessions including the running one.</summary>
    public TimeSpan GrandTotal { get; }

    /// <summary>The stopwatch with <paramref name="id"/> (including deleted), or null.</summary>
    public StopwatchDefinition? Find(string id) => AllStopwatches.FirstOrDefault(s => s.Id == id);

    /// <summary>True when the stopwatch with <paramref name="id"/> is running.</summary>
    public bool IsRunning(string id) => RunningId == id;

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
        if (RunningSession is { } running && running.StopwatchId == id)
        {
            return running.GetDuration(NowUtc);
        }

        var last = TodaySessions.LastOrDefault(s => s.StopwatchId == id && !s.IsOpen);
        return last?.GetDuration(NowUtc) ?? TimeSpan.Zero;
    }

    private TimeSpan Sum(IEnumerable<Session> sessions) =>
        sessions.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.GetDuration(NowUtc));
}
