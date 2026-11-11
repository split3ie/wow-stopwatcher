using Avalonia.Controls;

namespace WowStopwatcher.App.Shell;

/// <summary>Owns the single main window.</summary>
public interface IMainWindowService
{
    /// <summary>The main window, for dialogs that must be owned by it.</summary>
    Window Owner { get; }

    /// <summary>Shows, un-minimizes and activates the main window.</summary>
    void Show();
}
