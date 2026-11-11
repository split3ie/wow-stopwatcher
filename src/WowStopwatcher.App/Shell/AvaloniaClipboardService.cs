using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace WowStopwatcher.App.Shell;

/// <summary><see cref="IClipboardService"/> over the clipboard of the application's main window.</summary>
public sealed class AvaloniaClipboardService(IClassicDesktopStyleApplicationLifetime desktop) : IClipboardService
{
    public async Task<bool> SetTextAsync(string text)
    {
        if (TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard is not { } clipboard)
        {
            return false;
        }

        await clipboard.SetTextAsync(text);
        return true;
    }
}
