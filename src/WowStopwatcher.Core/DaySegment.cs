namespace WowStopwatcher.Core;

/// <summary>One session drawn on a day's timeline. <see cref="WidthFraction"/> is relative to the shared scale.</summary>
public sealed record DaySegment(string StopwatchId, string Color, TimeSpan Duration, double WidthFraction);
