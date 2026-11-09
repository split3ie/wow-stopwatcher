namespace WowStopwatcher.App.Features.UpdateCheck;

/// <summary>Outcome of a check; <see cref="Version"/> and <see cref="Url"/> are set only when Available.</summary>
public sealed record UpdateCheckResult(UpdateStatus Status, string? Version = null, string? Url = null);
