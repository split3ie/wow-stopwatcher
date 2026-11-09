using Avalonia.Controls;
using WowStopwatcher.App.ViewModels;
using WowStopwatcher.App.Views;

namespace WowStopwatcher.App.Shell;

/// <summary>Creates the main window once and shows it on request.</summary>
public sealed class MainWindowService : IMainWindowService
{
    public MainWindowService(MainViewModel viewModel, ShellOptions options)
    {
        Window = new MainWindow
        {
            DataContext = viewModel,
            CloseHidesWindow = options.CloseHidesWindow,
        };
    }

    /// <summary>The main window instance.</summary>
    public MainWindow Window { get; }

    public void Show()
    {
        Window.Show();
        if (Window.WindowState == WindowState.Minimized)
        {
            Window.WindowState = WindowState.Normal;
        }

        // Toggling Topmost helps bring the window in front of other apps.
        Window.Topmost = true;
        Window.Activate();
        Window.Topmost = false;
    }
}
