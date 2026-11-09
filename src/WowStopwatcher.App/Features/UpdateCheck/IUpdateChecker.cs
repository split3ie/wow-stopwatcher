namespace WowStopwatcher.App.Features.UpdateCheck;

/// <summary>Asks whether a newer version than the running one is published.</summary>
public interface IUpdateChecker
{
    /// <summary>Compares the latest release with <paramref name="currentVersion"/> (for example "0.1.0").</summary>
    Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken = default);
}
