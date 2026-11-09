namespace WowStopwatcher.Core;

/// <summary>The default policy: hides temporary stopwatches after a full unused local day (see <see cref="TemporaryStopwatchExpiry"/>).</summary>
public sealed class TemporaryStopwatchExpiryPolicy : IStopwatchExpiryPolicy
{
    /// <inheritdoc />
    public IReadOnlyList<string> FindExpired(
        IEnumerable<StopwatchDefinition> definitions,
        IReadOnlyDictionary<string, DateTimeOffset> latestStartPerStopwatch,
        IReadOnlySet<string> runningIds,
        DateOnly today,
        TimeZoneInfo zone) =>
        TemporaryStopwatchExpiry.FindExpired(definitions, latestStartPerStopwatch, runningIds, today, zone);
}
