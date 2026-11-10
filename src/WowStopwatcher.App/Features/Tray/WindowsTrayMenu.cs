using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Tray;

/// <summary>
/// The tray menu on Windows. Single click opens the custom <see cref="TrayMenuWindow"/>, double click starts the next
/// stopwatch, right click shows a plain native menu (Open, Stop all, Quit).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsTrayMenu : ITrayMenu
{
    private readonly IStopwatchState _state;
    private readonly IStopwatchControl _control;
    private readonly IMainWindowService _mainWindow;
    private readonly IAppLifetime _lifetime;
    private TrayMenuWindow? _menu;
    private DispatcherTimer? _pendingOpen;
    private long _menuClosedAt;

    public WindowsTrayMenu(IStopwatchState state, IStopwatchControl control, IMainWindowService mainWindow, IAppLifetime lifetime)
    {
        _state = state;
        _control = control;
        _mainWindow = mainWindow;
        _lifetime = lifetime;
    }

    public void Attach(TrayIcon tray)
    {
        // Right click: a plain native menu (left click is the custom menu).
        var open = new NativeMenuItem(Loc.T("tray.open"));
        open.Click += (_, _) => _mainWindow.Show();
        var stopAll = new NativeMenuItem(Loc.T("tray.stopAll"));
        stopAll.Click += (_, _) => _control.StopAll();
        var quit = new NativeMenuItem(Loc.T("tray.quit"));
        quit.Click += (_, _) => _lifetime.Quit();

        tray.Menu = [open, stopAll, new NativeMenuItemSeparator(), quit];
        tray.Clicked += OnClicked;
    }

    public void Update(StopwatchEngineSnapshot snapshot)
    {
    }

    private void OnClicked(object? sender, EventArgs e)
    {
        // Second click within the double-click time: next stopwatch, and the menu never opens.
        if (_pendingOpen is not null)
        {
            _pendingOpen.Stop();
            _pendingOpen = null;
            _control.StartNext();
            return;
        }

        if (_menu is not null)
        {
            _menu.Close();
            return;
        }

        // The click that deactivated an open menu also arrives here; treat it as "close".
        if (Environment.TickCount64 - _menuClosedAt < 300)
        {
            return;
        }

        // Capture the cursor now: it is the best available position of the tray icon.
        var anchor = GetCursorPos(out var point) ? new PixelPoint(point.X, point.Y) : default;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime()) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _pendingOpen = null;
            ShowMenu(anchor);
        };
        _pendingOpen = timer;
        timer.Start();
    }

    private void ShowMenu(PixelPoint anchor)
    {
        var menu = new TrayMenuWindow().Attach(_state, _control, _mainWindow.Show, _lifetime.Quit, anchor);
        menu.Closed += (_, _) =>
        {
            _menu = null;
            _menuClosedAt = Environment.TickCount64;
        };
        _menu = menu;
        menu.Show();
    }

    public void Dispose()
    {
        _pendingOpen?.Stop();
        _menu?.Close();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
