namespace WowStopwatcher.Core;

/// <summary>Housekeeping of the stopwatch engine: resets, periodic tick, reloading definitions, restoring hidden stopwatches.</summary>
public interface IStopwatchMaintenance
{
    /// <summary>Deletes today's sessions of the stopwatch (stopping it if running).</summary>
    void ResetToday(string id);

    /// <summary>Deletes all of today's sessions and stops every running stopwatch.</summary>
    void ResetAllToday();

    /// <summary>
    /// Call every ~50 ms to 1 s. Splits running sessions that crossed local midnight and
    /// switches "today" when the date changed. Raises <see cref="IStopwatchState.Changed"/> only if something changed.
    /// </summary>
    void Tick();

    /// <summary>
    /// Re-reads the definitions after a Settings change. Stops deleted stopwatches, re-sorts open sessions into
    /// normal and independent by the current flags, and keeps only the most recently started normal one.
    /// </summary>
    void ReloadDefinitions();

    /// <summary>
    /// Brings a hidden (deleted or expired) stopwatch back: it goes to the end of the visible list and its
    /// ActiveSinceUtc is set to now. Keeps its shortcut unless a visible stopwatch uses it, then takes the next free default.
    /// Returns false when the stopwatch is unknown or not deleted.
    /// </summary>
    bool Restore(string id);
}
