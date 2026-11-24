namespace WowStopwatcher.Core;

/// <summary>Remembers the last moment the app was known to be alive, so a gap (sleep, power loss) can be detected.</summary>
public interface ILastAliveStore
{
    /// <summary>The stored time, or null when none was written yet or it cannot be read.</summary>
    DateTimeOffset? Read();

    /// <summary>Stores the time (UTC).</summary>
    void Write(DateTimeOffset utc);
}
