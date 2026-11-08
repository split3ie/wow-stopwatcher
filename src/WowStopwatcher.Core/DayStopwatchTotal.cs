namespace WowStopwatcher.Core;

/// <summary>A stopwatch's total on one day.</summary>
/// <param name="Share">This total divided by the day total, clamped to 0..1 (0 when the day total is 0). Overlapping stopwatches can sum above 1.</param>
/// <param name="Goal">The stopwatch's current goal, or null.</param>
/// <param name="IsIndependent">True for an independent stopwatch.</param>
/// <param name="GoalReached">True when the stopwatch is not deleted, has a goal, and Total &gt;= Goal.</param>
public sealed record DayStopwatchTotal(
    string StopwatchId,
    string Name,
    string Color,
    bool IsDeleted,
    TimeSpan Total,
    double Share,
    TimeSpan? Goal,
    bool GoalReached,
    bool IsIndependent = false);
