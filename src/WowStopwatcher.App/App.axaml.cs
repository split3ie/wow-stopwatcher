using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using WowStopwatcher.App.Services;
using WowStopwatcher.App.Tray;
using WowStopwatcher.App.ViewModels;
using WowStopwatcher.App.Views;
using WowStopwatcher.Core;
using WowStopwatcher.Data;

namespace WowStopwatcher.App;

public partial class App : Application
{
    private MainWindow? _mainWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The app lives in the tray: closing the main window only hides it. Quit calls desktop.Shutdown().
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Composition root: everything is wired by hand.
            var database = SqliteDatabase.OpenDefault();
            var stopwatches = new SqliteStopwatchRepository(database);
            var sessions = new SqliteSessionRepository(database);
            var time = TimeProvider.System;
            var engine = new StopwatchEngine(stopwatches, sessions, time);

            var hotkeys = new GlobalHotkeyService(engine);
            _mainWindow = new MainWindow
            {
                DataContext = new MainViewModel(engine, stopwatches, sessions, time, hotkeys),
            };

            hotkeys.Start();

            var tray = new TrayController(engine, ShowMainWindow, () => desktop.Shutdown());
            tray.Start();

            Program.Instance?.StartListening(() => Dispatcher.UIThread.Post(ShowMainWindow));

            // On normal quit the running session is stopped and saved.
            desktop.Exit += (_, _) =>
            {
                hotkeys.Dispose();
                tray.Dispose();
                engine.StopAll();
            };

            // "--minimized" (autostart): keep the main window hidden, only the tray icon is visible.
            if (desktop.Args?.Contains("--minimized") != true)
            {
                ShowMainWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Shows, un-minimizes and activates the main window.</summary>
    private void ShowMainWindow()
    {
        if (_mainWindow is not { } window)
        {
            return;
        }

        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        // Toggling Topmost helps bring the window in front of other apps.
        window.Topmost = true;
        window.Activate();
        window.Topmost = false;
    }
}
