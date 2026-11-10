using System.Diagnostics;
using Avalonia.Controls.ApplicationLifetimes;

namespace WowStopwatcher.App.Shell;

/// <summary><see cref="IAppLifetime"/> over the classic desktop lifetime.</summary>
public sealed class DesktopAppLifetime(IClassicDesktopStyleApplicationLifetime desktop) : IAppLifetime
{
    public bool KeepsRunningSessions { get; private set; }

    public void Quit() => desktop.Shutdown();

    public bool CanRestart => !string.IsNullOrEmpty(Environment.ProcessPath);

    public void Restart()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        // "--restart" makes the new process wait for this one to release the single-instance lock.
        var info = new ProcessStartInfo(path) { UseShellExecute = false };
        info.ArgumentList.Add("--restart");
        using var process = Process.Start(info);
        QuitKeepingRunningSessions();
    }

    public void QuitKeepingRunningSessions()
    {
        KeepsRunningSessions = true;
        desktop.Shutdown();
    }
}
