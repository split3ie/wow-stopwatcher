namespace WowStopwatcher.App.Shell;

/// <summary>Default <see cref="ISensitiveActionGuard"/>: asks nothing.</summary>
public sealed class NullSensitiveActionGuard : ISensitiveActionGuard
{
    public Task<bool> ConfirmAsync() => Task.FromResult(true);
}
