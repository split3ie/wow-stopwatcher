namespace WowStopwatcher.App.Shell;

/// <summary>
/// What Settings needs from global shortcuts. The shell registers <see cref="NullShortcutService"/>; the Hotkeys feature
/// replaces it with the real service.
/// </summary>
public interface IShortcutService
{
    /// <summary>True only when the Hotkeys feature is part of this build. When false, Settings hides all shortcut UI.</summary>
    bool IsAvailable { get; }

    /// <summary>False when global hotkeys cannot work in this session (for example Linux on Wayland).</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Stopwatch ids whose shortcut could not be registered (for example taken by another app).
    /// When hotkeys are not supported, every stopwatch with a shortcut is listed.
    /// </summary>
    IReadOnlyList<string> FailedIds { get; }

    /// <summary>Raised, on any thread, after hotkeys were applied and <see cref="FailedIds"/> may have changed.</summary>
    event EventHandler? FailuresChanged;

    /// <summary>
    /// Unregisters all hotkeys (for example while a shortcut is being recorded). Calls are counted: hotkeys come back
    /// after as many <see cref="Resume"/> calls.
    /// </summary>
    void Suspend();

    /// <summary>Undoes one <see cref="Suspend"/> and re-registers the current shortcuts once none is left. Extra calls are ignored.</summary>
    void Resume();
}
