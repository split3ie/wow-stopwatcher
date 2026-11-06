namespace WowStopwatcher.Core;

/// <summary>What a Today timeline segment represents.</summary>
public enum TimelineSegmentKind
{
    /// <summary>A logged session (finished or live).</summary>
    Logged,

    /// <summary>The single grey section for the time left to reach goals.</summary>
    Remaining,
}
