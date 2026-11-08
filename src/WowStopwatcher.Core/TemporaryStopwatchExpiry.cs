namespace WowStopwatcher.Core;

/// <summary>
/// Decides which temporary stopwatches to hide. A stopwatch's last used day is the later of the local date of
/// its <see cref="StopwatchDefinition.ActiveSinceUtc"/> and the local date of its latest session start.
/// It expires when today is at least two days after that, so it disappears after one full unused day.
/// </summary>
public static class TemporaryStopwatchExpiry
{
    /// <summary>
    /// Ids of the stopwatches to hide: not deleted, not permanent, not running, and unused for a full local day.
    /// A stopwatch with neither ActiveSinceUtc nor sessions never expires.
    /// </summary>
    public static IReadOnlyList<string> FindExpired(
        IEnumerable<StopwatchDefinition> definitions,
        IReadOnlyDictionary<string, DateTimeOffset> latestStartPerStopwatch,
        IReadOnlySet<string> runningIds,
        DateOnly today,
        TimeZoneInfo zone)
    {
        var expired = new List<string>();
        foreach (var definition in definitions)
        {
            if (definition.IsDeleted || definition.IsPermanent || runningIds.Contains(definition.Id))
            {
                continue;
            }

            DateOnly? lastUsed = null;
            if (definition.ActiveSinceUtc is { } active)
            {
                lastUsed = LocalDays.ToLocalDate(active, zone);
            }

            if (latestStartPerStopwatch.TryGetValue(definition.Id, out var start))
            {
                var day = LocalDays.ToLocalDate(start, zone);
                if (lastUsed is null || day > lastUsed)
                {
                    lastUsed = day;
                }
            }

            if (lastUsed is { } last && today >= last.AddDays(2))
            {
                expired.Add(definition.Id);
            }
        }

        return expired;
    }
}
