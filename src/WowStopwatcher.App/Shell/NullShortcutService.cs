namespace WowStopwatcher.App.Shell;

/// <summary>The shortcut service of a build without the Hotkeys feature: not available, does nothing.</summary>
public sealed class NullShortcutService : IShortcutService
{
    public bool IsAvailable => false;

    public bool IsSupported => false;

    public IReadOnlyList<string> FailedIds => [];

    public event EventHandler? FailuresChanged
    {
        add { }
        remove { }
    }

    public void Suspend()
    {
    }

    public void Resume()
    {
    }
}
