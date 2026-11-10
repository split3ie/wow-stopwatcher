namespace WowStopwatcher.Core;

/// <summary>A stopwatch's goal progress for today.</summary>
/// <param name="StopwatchId">The stopwatch.</param>
/// <param name="Goal">The target of the cycle or day being filled.</param>
/// <param name="Progress">Counted time toward <paramref name="Goal"/>.</param>
/// <param name="IsReached">True when a goal was reached today.</param>
/// <param name="ReachedCount">How many goals were reached today.</param>
/// <param name="OverGoal">Time counted past the goal today; zero when not reached.</param>
/// <param name="CarriedFromUtc">For a per-cycle goal, when the open cycle started if that was on an earlier day (the progress counts toward that day's goal); otherwise null.</param>
public sealed record GoalStatus(
    string StopwatchId,
    TimeSpan Goal,
    TimeSpan Progress,
    bool IsReached,
    int ReachedCount,
    TimeSpan OverGoal,
    DateTimeOffset? CarriedFromUtc)
{
    /// <summary>Time still needed to reach the goal; zero once reached today.</summary>
    public TimeSpan Remaining => IsReached ? TimeSpan.Zero : Max(TimeSpan.Zero, Goal - Progress);

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
