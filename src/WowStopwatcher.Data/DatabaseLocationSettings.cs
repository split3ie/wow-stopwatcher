namespace WowStopwatcher.Data;

/// <summary>The content of <c>location.json</c>. A null folder means the default folder.</summary>
public sealed record DatabaseLocationSettings(string? DatabaseFolder, PendingStep? Pending);
