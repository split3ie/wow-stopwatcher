namespace WowStopwatcher.Core;

/// <summary>
/// One piece of the Today timeline track. Positions are fractions (0..1) of the track width.
/// There is one <see cref="TimelineSegmentKind.Remaining"/> piece per unfinished goal; it carries that stopwatch's id and color.
/// </summary>
/// <param name="Kind">Logged session or the time left to one stopwatch's goal.</param>
/// <param name="StopwatchId">Owning stopwatch (for a remaining piece, the stopwatch whose goal it is).</param>
/// <param name="Color">Hex color of the stopwatch.</param>
/// <param name="Duration">Session duration (logged) or time left to that stopwatch's goal (remaining).</param>
/// <param name="IsLive">True for the running session.</param>
/// <param name="Title">"English · session", "English · running" or "English · left to goal".</param>
/// <param name="SubText">Only for a remaining piece: "Goal 5h"; otherwise null.</param>
/// <param name="Start">Start fraction of the track.</param>
/// <param name="Width">Width fraction of the track.</param>
/// <param name="Capped">True on the first remaining piece when the remaining sections were shortened to 35% of the track.</param>
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
    bool Capped,
    IReadOnlyList<TimelineLane>? Lanes = null)
{
    /// <summary>
    /// The stopwatches running during a logged piece, one stripe each (normal first). More than one when they overlap.
    /// Empty for the remaining section. <see cref="StopwatchId"/> and <see cref="Color"/> repeat the first lane.
    /// </summary>
    public IReadOnlyList<TimelineLane> Lanes { get; } = Lanes ?? [];

    /// <summary>Center fraction of the segment.</summary>
    public double Center => Start + Width / 2;
}
