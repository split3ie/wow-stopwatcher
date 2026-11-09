using Avalonia.Controls.ApplicationLifetimes;

namespace WowStopwatcher.App.Shell;

/// <summary><see cref="IAppLifetime"/> over the classic desktop lifetime.</summary>
public sealed class DesktopAppLifetime(IClassicDesktopStyleApplicationLifetime desktop) : IAppLifetime
{
    public bool KeepsRunningSessions { get; private set; }

    public void Quit() => desktop.Shutdown();

    public void QuitKeepingRunningSessions()
    {
        KeepsRunningSessions = true;
        desktop.Shutdown();
    }
}
