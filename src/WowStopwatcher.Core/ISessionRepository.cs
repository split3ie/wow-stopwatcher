namespace WowStopwatcher.Core;

/// <summary>Persistent storage of sessions. Ranges are half-open [from, to) and match on the session start.</summary>
public interface ISessionRepository
{
    /// <summary>Stores a new open session (no stop time) and returns its id.</summary>
    long InsertOpen(string stopwatchId, DateTimeOffset startUtc);

    /// <summary>Sets the stop time of a session.</summary>
    void Close(long sessionId, DateTimeOffset stopUtc);

    /// <summary>All sessions without a stop time, ordered by start.</summary>
    IReadOnlyList<Session> GetOpen();

    /// <summary>The session with the latest start, or null when there are none.</summary>
    Session? GetLatest();

    /// <summary>The session with the latest start among the given stopwatches, or null when there are none (or no ids).</summary>
    Session? GetLatestForStopwatches(IReadOnlyCollection<string> stopwatchIds);

    /// <summary>The latest session start per stopwatch id. Stopwatches without sessions are absent.</summary>
    IReadOnlyDictionary<string, DateTimeOffset> GetLatestStartPerStopwatch();

    /// <summary>Sessions whose start is in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>), ordered by start.</summary>
    IReadOnlyList<Session> GetInRange(DateTimeOffset fromUtc, DateTimeOffset toUtc);

    /// <summary>Distinct local dates (in <paramref name="zone"/>) that have at least one session, newest first.</summary>
    IReadOnlyList<DateOnly> GetLocalDays(TimeZoneInfo zone);

    /// <summary>Deletes the stopwatch's sessions whose start is in [from, to). Returns the number deleted.</summary>
    int DeleteForStopwatchInRange(string stopwatchId, DateTimeOffset fromUtc, DateTimeOffset toUtc);

    /// <summary>Deletes all sessions that start on any of the given local days. Returns the number deleted.</summary>
    int DeleteForLocalDays(IEnumerable<DateOnly> days, TimeZoneInfo zone);

    /// <summary>Deletes every session. Returns the number deleted.</summary>
    int DeleteAll();
}
