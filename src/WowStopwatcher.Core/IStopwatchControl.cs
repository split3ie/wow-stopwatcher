namespace WowStopwatcher.Core;

/// <summary>Starting and stopping stopwatches.</summary>
public interface IStopwatchControl
{
    /// <summary>
    /// Starts the stopwatch. A normal stopwatch stops the running normal one; an independent stopwatch
    /// touches no other. Does nothing if it is unknown, deleted or already running.
    /// </summary>
    void Start(string id);

    /// <summary>Stops the stopwatch if it is running, otherwise starts it (see <see cref="Start"/>).</summary>
    void Toggle(string id);

    /// <summary>Stops the running normal stopwatch and all running independent stopwatches.</summary>
    void StopAll();

    /// <summary>
    /// Cycles over the non-deleted, non-independent stopwatches only; independent ones are untouched.
    /// If a normal one runs, stops it and starts the next by SortOrder (wrapping around).
    /// If none runs, starts the last used normal stopwatch, else the first. Does nothing without normal stopwatches.
    /// </summary>
    void StartNext();
}
