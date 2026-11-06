namespace WowStopwatcher.Core;

/// <summary>
/// One run of a stopwatch. <see cref="StopUtc"/> is null while the stopwatch is running.
/// </summary>
public sealed record Session(long Id, string StopwatchId, DateTimeOffset StartUtc, DateTimeOffset? StopUtc)
{
    /// <summary>True while the session is still running.</summary>
    public bool IsOpen => StopUtc is null;

    /// <summary>Duration of the session; an open session is measured up to <paramref name="nowUtc"/>. Never negative.</summary>
    public TimeSpan GetDuration(DateTimeOffset nowUtc)
    {
        var duration = (StopUtc ?? nowUtc) - StartUtc;
        return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
    }
}
