namespace WowStopwatcher.Core;

/// <summary>Statistics of one local day.</summary>
/// <param name="Date">The local date.</param>
/// <param name="Segments">Sessions in chronological order.</param>
/// <param name="Totals">Per-stopwatch totals ordered by SortOrder: stopwatches with time that day plus all non-deleted goal stopwatches.</param>
/// <param name="GoalsMet">Number of non-deleted goal stopwatches that reached their goal.</param>
/// <param name="GoalCount">Number of non-deleted stopwatches with a goal.</param>
/// <param name="SessionCount">Number of sessions that day.</param>
/// <param name="Total">Sum of all sessions that day.</param>
public sealed record DayStatistics(
    DateOnly Date,
    IReadOnlyList<DaySegment> Segments,
    IReadOnlyList<DayStopwatchTotal> Totals,
    int GoalsMet,
    int GoalCount,
    int SessionCount,
    TimeSpan Total);
