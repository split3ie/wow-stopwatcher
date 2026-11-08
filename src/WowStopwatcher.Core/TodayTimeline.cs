namespace WowStopwatcher.Core;

/// <summary>Layout of the Today timeline track, produced by <see cref="TodayTimelineBuilder"/>.</summary>
public sealed class TodayTimeline
{
    public TodayTimeline(
        IReadOnlyList<TimelineSegment> segments,
        double nowPosition,
        IReadOnlyList<TimelineGoalPin> pins,
        int maxPinLevel,
        TimeSpan logged,
        TimeSpan remaining,
        IReadOnlyList<RemainingGoal> remainingGoals,
        bool remainingCapped)
    {
        Segments = segments;
        NowPosition = nowPosition;
        Pins = pins;
        MaxPinLevel = maxPinLevel;
        Logged = logged;
        Remaining = remaining;
        RemainingGoals = remainingGoals;
        RemainingCapped = remainingCapped;
    }

    /// <summary>Logged pieces in chronological order, then one remaining piece per unfinished goal, nearest goal first.</summary>
    public IReadOnlyList<TimelineSegment> Segments { get; }

    /// <summary>Position of the "now" marker (fraction): the share of the track taken by logged time.</summary>
    public double NowPosition { get; }

    /// <summary>Goal pins sorted by position.</summary>
    public IReadOnlyList<TimelineGoalPin> Pins { get; }

    /// <summary>Highest pin level in use (0 when there are no pins or no overlaps).</summary>
    public int MaxPinLevel { get; }

    /// <summary>Total logged time today.</summary>
    public TimeSpan Logged { get; }

    /// <summary>Total time left to all goals.</summary>
    public TimeSpan Remaining { get; }

    /// <summary>Goals not yet reached, sorted by remaining time descending.</summary>
    public IReadOnlyList<RemainingGoal> RemainingGoals { get; }

    /// <summary>True when the remaining section was shortened to 35% of the track.</summary>
    public bool RemainingCapped { get; }
}
