namespace WowStopwatcher.Core;

/// <summary>Per-day statistics (newest day first) with the scale shared by all days.</summary>
/// <param name="Days">Days, newest first.</param>
/// <param name="ScaleMinutes">Full width of a day timeline in minutes: max(540, ceil(maxDayMinutes / 180) * 180).</param>
/// <param name="Ticks">Four labels at 0, 1/3, 2/3 and 3/3 of the scale in whole hours ("0h", "3h", "6h", "9h").</param>
public sealed record StatisticsResult(
    IReadOnlyList<DayStatistics> Days,
    int ScaleMinutes,
    IReadOnlyList<StatisticsTick> Ticks);
