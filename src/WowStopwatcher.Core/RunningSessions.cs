namespace WowStopwatcher.Core;

/// <summary>
/// The open sessions of the engine: at most one normal session plus independent ones keyed by stopwatch id.
/// Not thread-safe; the engine guards it with its lock.
/// </summary>
internal sealed class RunningSessions(int maxConcurrent)
{
    private readonly Dictionary<string, Session> _independent = [];

    public Session? Normal { get; set; }

    public IReadOnlyDictionary<string, Session> Independent => _independent;

    public int OpenCount => (Normal is null ? 0 : 1) + _independent.Count;

    // A normal start replaces the running normal one, so it needs no free slot then.
    public bool HasRoomFor(bool isIndependent) => OpenCount < maxConcurrent || (Normal is not null && !isIndependent);

    public bool IsRunning(string stopwatchId) => Normal?.StopwatchId == stopwatchId || _independent.ContainsKey(stopwatchId);

    public void SetIndependent(string stopwatchId, Session session) => _independent[stopwatchId] = session;

    public bool TryRemoveIndependent(string stopwatchId, out Session session) => _independent.Remove(stopwatchId, out session!);

    public void Clear()
    {
        Normal = null;
        _independent.Clear();
    }

    public IEnumerable<Session> AllOpen() =>
        (Normal is { } running ? [running] : Enumerable.Empty<Session>()).Concat(_independent.Values);

    // Should not happen; if more than the limit are open, these are all but the most recently started ones.
    public IReadOnlyList<Session> SessionsOverLimit() =>
        AllOpen().OrderByDescending(o => o.StartUtc).ThenByDescending(o => o.Id).Skip(maxConcurrent).ToList();
}
