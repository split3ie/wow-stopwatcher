namespace WowStopwatcher.Core;

/// <summary>A change of a stopwatch's daily goal, recorded so past days can be judged against the goal they had.</summary>
/// <param name="StopwatchId">The stopwatch whose goal changed.</param>
/// <param name="ChangedUtc">When the goal was changed.</param>
/// <param name="PreviousGoal">The goal before the change; null means there was no goal.</param>
public sealed record GoalChange(string StopwatchId, DateTimeOffset ChangedUtc, TimeSpan? PreviousGoal);
