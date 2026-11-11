using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace WowStopwatcher.App.Shell;

/// <summary><see cref="IFolderLauncher"/> over the launcher of the application's main window (Explorer, Finder or the default file manager).</summary>
public sealed class AvaloniaFolderLauncher(IClassicDesktopStyleApplicationLifetime desktop) : IFolderLauncher
{
    public async Task<bool> OpenAsync(string path)
    {
        if (!Directory.Exists(path) || TopLevel.GetTopLevel(desktop.MainWindow)?.Launcher is not { } launcher)
        {
            return false;
        }

        try
        {
            return await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }
}
