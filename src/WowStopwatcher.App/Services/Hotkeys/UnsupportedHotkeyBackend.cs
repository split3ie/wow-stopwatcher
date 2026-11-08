namespace WowStopwatcher.App.Services.Hotkeys;

/// <summary>Used where global hotkeys are not available. Registers nothing; the facade reports every shortcut as failed.</summary>
internal sealed class UnsupportedHotkeyBackend : IHotkeyBackend
{
    public bool IsSupported => false;

    public IReadOnlyList<string> FailedIds => [];

    public event Action? FailuresChanged
    {
        add { }
        remove { }
    }

    public void Start(Action<string> onPressed)
    {
    }

    public void SetHotkeys(IReadOnlyList<(string Id, ShortcutKey Key)> hotkeys)
    {
    }

    public void Dispose()
    {
    }
}
