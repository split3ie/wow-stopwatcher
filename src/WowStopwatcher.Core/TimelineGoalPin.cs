namespace WowStopwatcher.Core;

/// <summary>Marks where a stopwatch's cumulative total crossed its daily goal on the Today timeline.</summary>
/// <param name="StopwatchId">The stopwatch that reached its goal.</param>
/// <param name="Color">Stopwatch color.</param>
/// <param name="Title">"English · goal 5h reached".</param>
/// <param name="SubText">"During its 2nd session (running now) · 3h 10m into today's log".</param>
/// <param name="Position">Fraction (0..1) of the track width.</param>
/// <param name="Level">Vertical level, 0 is the lowest; raised when pins would overlap.</param>
public sealed record TimelineGoalPin(
    string StopwatchId,
    string Color,
    string Title,
    string SubText,
    double Position,
    int Level);
