namespace WowStopwatcher.Core;

/// <summary>A stretch of time during which the set of running stopwatches is constant.</summary>
/// <param name="StartUtc">Start of the stretch.</param>
/// <param name="Duration">Length of the stretch (always positive).</param>
/// <param name="Lanes">The running stopwatches: normal ones first, then independent ones, each group by SortOrder.</param>
public sealed record SessionSlice(DateTimeOffset StartUtc, TimeSpan Duration, IReadOnlyList<TimelineLane> Lanes)
{
    /// <summary>End of the stretch.</summary>
    public DateTimeOffset EndUtc => StartUtc + Duration;

    /// <summary>True when any lane is live.</summary>
    public bool IsLive => Lanes.Any(l => l.IsLive);
}
