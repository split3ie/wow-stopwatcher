namespace WowStopwatcher.App.Features.Hotkeys;

/// <summary>One operating system's way of registering system-wide hotkeys. Calls must not block.</summary>
internal interface IHotkeyBackend : IDisposable
{
    /// <summary>False when hotkeys cannot work at all in this session (for example Wayland).</summary>
    bool IsSupported { get; }

    /// <summary>Stopwatch ids whose shortcut could not be registered by the last <see cref="SetHotkeys"/>.</summary>
    IReadOnlyList<string> FailedIds { get; }

    /// <summary>Raised, on any thread, after <see cref="FailedIds"/> was updated by an applied <see cref="SetHotkeys"/>.</summary>
    event Action? FailuresChanged;

    /// <summary>Starts the backend. <paramref name="onPressed"/> receives the stopwatch id of a pressed hotkey, on any thread.</summary>
    void Start(Action<string> onPressed);

    /// <summary>Replaces all registered hotkeys with the given ones (an empty list unregisters everything). Applied asynchronously.</summary>
    void SetHotkeys(IReadOnlyList<(string Id, ShortcutKey Key)> hotkeys);
}
