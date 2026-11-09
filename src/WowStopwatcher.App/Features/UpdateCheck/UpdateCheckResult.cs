namespace WowStopwatcher.App.Features.UpdateCheck;

/// <summary>
/// Outcome of a check. The other values are set only when Available; the package values are null
/// when the release has no asset for this platform.
/// </summary>
public sealed record UpdateCheckResult(
    UpdateStatus Status,
    string? Version = null,
    string? Url = null,
    string? PackageUrl = null,
    string? PackageFileName = null,
    string? ChecksumUrl = null);
