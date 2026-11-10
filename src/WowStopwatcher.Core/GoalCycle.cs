namespace WowStopwatcher.Core;

/// <summary>
/// One goal cycle of a stopwatch: an amount of time that fills across any number of days.
/// A cycle starts at an anchor instant or when the previous cycle was reached, and is reached when the counted time in it equals the goal.
/// </summary>
/// <param name="StopwatchId">The stopwatch the cycle belongs to.</param>
/// <param name="StartUtc">When the cycle started.</param>
/// <param name="Date">The local date of <paramref name="StartUtc"/>; the day the cycle and its mark belong to.</param>
/// <param name="Goal">The target in effect when the cycle was reached; for an open cycle, the target at the build time.</param>
/// <param name="Progress">Counted time in the cycle; equals <paramref name="Goal"/> for a reached cycle.</param>
/// <param name="ReachedUtc">When the goal was reached; null while the cycle is open.</param>
public sealed record GoalCycle(
    string StopwatchId,
    DateTimeOffset StartUtc,
    DateOnly Date,
    TimeSpan Goal,
    TimeSpan Progress,
    DateTimeOffset? ReachedUtc)
{
    /// <summary>True when the goal of the cycle has been reached.</summary>
    public bool IsReached => ReachedUtc is not null;
}
