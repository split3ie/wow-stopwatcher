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

    public async Task<string?> TryGetTextAsync()
    {
        if (TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard is not { } clipboard)
        {
            return null;
        }

        try
        {
            return await clipboard.TryGetTextAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Runtime.InteropServices.COMException)
        {
            // Another program holds the clipboard; treat it as not readable.
            return null;
        }
    }

    public async Task ClearAsync()
    {
        if (TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard is { } clipboard)
        {
            await clipboard.ClearAsync();
        }
    }
}
