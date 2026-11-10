namespace WowStopwatcher.Core;

/// <summary>One stopwatch's share of a day segment, with its place in the normal and the combined day bar.</summary>
/// <param name="SegmentIndex">Index of the segment in the day.</param>
/// <param name="LaneIndex">Index of the piece inside its segment (0 for a segment without lanes).</param>
/// <param name="LaneCount">Number of pieces in the segment (at least 1).</param>
/// <param name="StopwatchId">The stopwatch the piece belongs to.</param>
/// <param name="Color">Color of the stopwatch.</param>
/// <param name="Duration">Duration of the whole segment (every piece of an overlap carries the full time).</param>
/// <param name="NormalStart">Start of the segment as a fraction of the bar.</param>
/// <param name="Width">Width of the segment as a fraction of the bar.</param>
/// <param name="CombinedStart">Start of the piece in the combined bar.</param>
/// <param name="Group">Index of the stopwatch group, in order of first appearance.</param>
/// <param name="GroupStart">Start of the group in the combined bar.</param>
/// <param name="GroupWidth">Width of the whole group.</param>
/// <param name="GroupDuration">Total time of the group: the full time of the stopwatch that day.</param>
/// <param name="IsGroupEnd">True for the last piece of its group.</param>
public sealed record CombinedPiece(
    int SegmentIndex,
    int LaneIndex,
    int LaneCount,
    string StopwatchId,
    string Color,
    TimeSpan Duration,
    double NormalStart,
    double Width,
    double CombinedStart,
    int Group,
    double GroupStart,
    double GroupWidth,
    TimeSpan GroupDuration,
    bool IsGroupEnd);
