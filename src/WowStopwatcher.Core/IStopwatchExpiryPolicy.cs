namespace WowStopwatcher.Core;

/// <summary>Decides which stopwatches the engine hides automatically.</summary>
public interface IStopwatchExpiryPolicy
{
    /// <summary>Ids of the stopwatches to hide, given the latest session start per stopwatch and the ids currently running.</summary>
    IReadOnlyList<string> FindExpired(
        IEnumerable<StopwatchDefinition> definitions,
        IReadOnlyDictionary<string, DateTimeOffset> latestStartPerStopwatch,
        IReadOnlySet<string> runningIds,
        DateOnly today,
        TimeZoneInfo zone);
}
