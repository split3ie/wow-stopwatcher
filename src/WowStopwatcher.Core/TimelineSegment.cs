namespace WowStopwatcher.Core;

/// <summary>
/// One piece of the Today timeline track. Positions are fractions (0..1) of the track width.
/// For <see cref="TimelineSegmentKind.Remaining"/>, <see cref="StopwatchId"/> is null and <see cref="Color"/> is grey.
/// </summary>
/// <param name="Kind">Logged session or remaining-to-goals section.</param>
/// <param name="StopwatchId">Owning stopwatch for logged pieces, otherwise null.</param>
/// <param name="Color">Hex color of the stopwatch, or grey for the remaining section.</param>
/// <param name="Duration">Session duration (logged) or total time left to goals (remaining).</param>
/// <param name="IsLive">True for the running session.</param>
/// <param name="Title">"English · session", "English · running" or "Left to 2 goals".</param>
/// <param name="SubText">Only for the remaining section: the goal breakdown; otherwise null.</param>
/// <param name="Start">Start fraction of the track.</param>
/// <param name="Width">Width fraction of the track.</param>
/// <param name="Capped">True when the remaining section was shortened to 35% of the track.</param>
public sealed record TimelineSegment(
    TimelineSegmentKind Kind,
    string? StopwatchId,
    string Color,
    TimeSpan Duration,
    bool IsLive,
    string Title,
    string? SubText,
    double Start,
    double Width,
    bool Capped)
{
    /// <summary>Center fraction of the segment.</summary>
    public double Center => Start + Width / 2;
}
