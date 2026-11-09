namespace WowStopwatcher.App.Features.UpdateCheck;

/// <summary>Downloads an available update and replaces the running app with it.</summary>
public interface IUpdateInstaller
{
    /// <summary>True when <paramref name="update"/> can be installed in place on this machine.</summary>
    bool CanInstall(UpdateCheckResult update);

    /// <summary>Downloads, verifies and installs the update. Throws with a clear message on failure; the app is left as it was.</summary>
    Task InstallAsync(UpdateCheckResult update, IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>Starts the installed app with <c>--after-update</c>. The caller then quits this instance.</summary>
    void StartNewInstance();
}
