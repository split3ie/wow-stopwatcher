namespace WowStopwatcher.Core;

/// <summary>A manual reset of a per-cycle goal: the open cycle ends as cancelled and a new one starts at the reset instant.</summary>
/// <param name="StopwatchId">The stopwatch whose goal cycle was reset.</param>
/// <param name="ResetUtc">When the reset happened; the start of the new cycle.</param>
public sealed record GoalReset(string StopwatchId, DateTimeOffset ResetUtc);
