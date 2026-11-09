namespace WowStopwatcher.Core;

/// <summary>Read-only view of the stopwatch engine and its change notification.</summary>
public interface IStopwatchState
{
    /// <summary>Raised after any state change (start, stop, reset, midnight split, reload).</summary>
    event EventHandler? Changed;

    /// <summary>A consistent view of the engine at the current time. Cheap; call it on every UI tick.</summary>
    StopwatchEngineSnapshot GetSnapshot();
}
