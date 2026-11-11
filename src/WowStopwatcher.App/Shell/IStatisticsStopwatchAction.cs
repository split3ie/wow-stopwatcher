namespace WowStopwatcher.App.Shell;

/// <summary>
/// A button a feature adds to a stopwatch's row in an expanded Statistics day. Register it as a singleton;
/// without registrations Statistics shows no button.
/// </summary>
public interface IStatisticsStopwatchAction
{
    /// <summary>Button text.</summary>
    string Label { get; }

    /// <summary>True when the button belongs on the row of this stopwatch.</summary>
    bool AppliesTo(string stopwatchId);

    /// <summary>Runs the action for the stopwatch on the given local day.</summary>
    void Invoke(string stopwatchId, DateOnly date);
}
