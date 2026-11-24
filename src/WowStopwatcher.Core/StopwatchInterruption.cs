namespace WowStopwatcher.Core;

/// <summary>Running stopwatches that were stopped because the app was not alive for a while (sleep, power loss).</summary>
/// <param name="StoppedAtUtc">When the sessions were stopped: the last moment the app was known to be alive.</param>
/// <param name="StopwatchIds">The stopped stopwatches, the normal one first.</param>
public sealed record StopwatchInterruption(DateTimeOffset StoppedAtUtc, IReadOnlyList<string> StopwatchIds);
