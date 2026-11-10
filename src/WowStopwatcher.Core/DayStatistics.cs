namespace WowStopwatcher.Core;

/// <summary>Statistics of one local day.</summary>
/// <param name="Date">The local date.</param>
/// <param name="Segments">Stretches of the day with a constant set of running stopwatches, in chronological order.</param>
/// <param name="Totals">Per-stopwatch totals ordered by SortOrder: stopwatches with time that day plus all non-deleted goal stopwatches.</param>
/// <param name="GoalsMet">Number of goal stopwatches that reached their goal (deleted ones count only on days where they have time).</param>
/// <param name="GoalCount">Number of non-deleted stopwatches with a goal, plus deleted goal stopwatches that have time that day.</param>
/// <param name="SessionCount">Number of sessions that day.</param>
/// <param name="Total">Wall-clock time any stopwatch ran that day (overlaps count once).</param>
public sealed record DayStatistics(
    DateOnly Date,
    IReadOnlyList<DaySegment> Segments,
    IReadOnlyList<DayStopwatchTotal> Totals,
    int GoalsMet,
    int GoalCount,
    int SessionCount,
    TimeSpan Total);
