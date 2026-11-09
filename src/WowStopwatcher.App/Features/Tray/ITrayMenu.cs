using Avalonia.Controls;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.Features.Tray;

/// <summary>How the tray icon reacts to clicks and what menu it offers. One implementation per operating system. Used on the UI thread.</summary>
internal interface ITrayMenu : IDisposable
{
    /// <summary>Sets up the menu and click handling on the tray icon before it is shown.</summary>
    void Attach(TrayIcon tray);

    /// <summary>Called about every second and on every engine change, so the menu can follow the stopwatches.</summary>
    void Update(StopwatchEngineSnapshot snapshot);
}
