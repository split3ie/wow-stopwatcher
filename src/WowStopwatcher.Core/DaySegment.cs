namespace WowStopwatcher.Core;

/// <summary>
/// One stretch of a day's timeline where the set of running stopwatches is constant.
/// <see cref="WidthFraction"/> is relative to the shared scale. <see cref="StopwatchId"/> and <see cref="Color"/>
/// repeat the first lane; <see cref="Lanes"/> lists every running stopwatch (several when they overlap).
/// </summary>
public sealed record DaySegment(
    string StopwatchId,
    string Color,
    TimeSpan Duration,
    double WidthFraction,
    IReadOnlyList<TimelineLane> Lanes);
