namespace WowStopwatcher.Core;

/// <summary>One stopwatch running during a stretch of time; drawn as one horizontal stripe (lane) of a bar piece.</summary>
/// <param name="StopwatchId">The running stopwatch.</param>
/// <param name="Color">Hex color of the stopwatch.</param>
/// <param name="IsLive">True when the session in this lane is still open.</param>
public sealed record TimelineLane(string StopwatchId, string Color, bool IsLive);
