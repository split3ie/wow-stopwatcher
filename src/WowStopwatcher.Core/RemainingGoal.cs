namespace WowStopwatcher.Core;

/// <summary>Time still needed today to reach one stopwatch's goal.</summary>
public sealed record RemainingGoal(string StopwatchId, string Name, string Color, TimeSpan Remaining);
