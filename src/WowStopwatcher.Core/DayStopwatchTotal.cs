namespace WowStopwatcher.Core;

/// <summary>A stopwatch's total on one day.</summary>
/// <param name="Share">This total divided by the day total, clamped to 0..1 (0 when the day total is 0). Overlapping stopwatches can sum above 1.</param>
/// <param name="Goal">The stopwatch's current goal, or null.</param>
/// <param name="IsIndependent">True for an independent stopwatch.</param>
/// <param name="GoalReached">True when the stopwatch has a goal and Total &gt;= Goal.</param>
/// <param name="OvertimeShare">The part of <paramref name="Share"/> beyond the goal ((Total - Goal) / day total, clamped to 0..Share); 0 when the goal is not reached or there is no goal.</param>
/// <param name="GoalsReachedCount">How many goals were reached on this day: 0 or 1 for an each-day goal, any number of cycles for a per-cycle goal.</param>
/// <param name="GoalReachedUtc">Per-cycle goals only: when the first cycle starting on this day was reached; null otherwise.</param>
/// <param name="CycleProgress">Per-cycle goals only: progress of the still-open cycle that started on this day, else of a cancelled one; null otherwise.</param>
/// <param name="CycleCancelled">Per-cycle goals only: true when a cycle that started on this day was cancelled by a reset.</param>
public sealed record DayStopwatchTotal(
    string StopwatchId,
    string Name,
    string Color,
    bool IsDeleted,
    TimeSpan Total,
    double Share,
    TimeSpan? Goal,
    bool GoalReached,
    bool IsIndependent = false,
    double OvertimeShare = 0,
    int GoalsReachedCount = 0,
    DateTimeOffset? GoalReachedUtc = null,
    TimeSpan? CycleProgress = null,
    bool CycleCancelled = false);
