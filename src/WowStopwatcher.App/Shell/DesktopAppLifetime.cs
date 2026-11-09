using Avalonia.Controls.ApplicationLifetimes;

namespace WowStopwatcher.App.Shell;

/// <summary><see cref="IAppLifetime"/> over the classic desktop lifetime.</summary>
public sealed class DesktopAppLifetime(IClassicDesktopStyleApplicationLifetime desktop) : IAppLifetime
{
    public void Quit() => desktop.Shutdown();
}
