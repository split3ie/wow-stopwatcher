namespace WowStopwatcher.Data;

/// <summary>
/// Result of reading <c>location.json</c>. A missing file gives empty settings and no problem.
/// A damaged file gives empty settings and a <see cref="Problem"/> message; it is never treated as "default" silently.
/// </summary>
public sealed record LocationReadResult(DatabaseLocationSettings Settings, string? Problem)
{
    /// <summary>True when the file exists but could not be read.</summary>
    public bool IsDamaged => Problem is not null;
}
