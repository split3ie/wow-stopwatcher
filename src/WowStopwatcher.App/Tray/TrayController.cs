using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.Tray;

/// <summary>
/// Owns the tray icon: tooltip, icon color, single click (custom menu) and double click (next stopwatch).
/// Must be created and used on the UI thread.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly StopwatchEngine _engine;
    private readonly Action _openMain;
    private readonly Action _quit;
    private readonly DispatcherTimer _tooltipTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private TrayIcon? _tray;
    private TrayMenuWindow? _menu;
    private DispatcherTimer? _pendingOpen;
    private long _menuClosedAt;
    private string? _iconKey;

    public TrayController(StopwatchEngine engine, Action openMain, Action quit)
    {
        _engine = engine;
        _openMain = openMain;
        _quit = quit;
    }

    public void Start()
    {
        // Right click: a plain native menu (left click is the custom menu).
        var open = new NativeMenuItem("Open WowStopwatcher");
        open.Click += (_, _) => _openMain();
        var stopAll = new NativeMenuItem("Stop all");
        stopAll.Click += (_, _) => _engine.StopAll();
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => _quit();

        _tray = new TrayIcon
        {
            ToolTipText = "WowStopwatcher",
            Menu = [open, stopAll, new NativeMenuItemSeparator(), quit],
            IsVisible = true,
        };
        _tray.Clicked += OnClicked;
        TrayIcon.SetIcons(Application.Current!, [_tray]);

        UpdateIconAndTooltip();
        _tooltipTimer.Tick += (_, _) => UpdateIconAndTooltip();
        _tooltipTimer.Start();
        _engine.Changed += OnEngineChanged;
    }

    private void OnEngineChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(UpdateIconAndTooltip);

    private void UpdateIconAndTooltip()
    {
        if (_tray is null)
        {
            return;
        }

        var snapshot = _engine.GetSnapshot();
        var running = snapshot.RunningId is { } id ? snapshot.Find(id) : null;

        var tooltip = running is null
            ? "WowStopwatcher"
            : $"WowStopwatcher · {running.Name} {TimeFormat.Clock(snapshot.RunningElapsed)}";
        if (_tray.ToolTipText != tooltip)
        {
            _tray.ToolTipText = tooltip;
        }

        // Regenerate the bitmap only when the running stopwatch (or its color) changes.
        var key = running is null ? "" : $"{running.Id}:{running.Color}";
        if (key == _iconKey)
        {
            return;
        }

        _iconKey = key;
        Color? color = running is not null && Color.TryParse(running.Color, out var parsed) ? parsed : null;
        _tray.Icon = TrayIconRenderer.Render(color);
    }

    private void OnClicked(object? sender, EventArgs e)
    {
        // Second click within the double-click time: next stopwatch, and the menu never opens.
        if (_pendingOpen is not null)
        {
            _pendingOpen.Stop();
            _pendingOpen = null;
            _engine.StartNext();
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
        var menu = new TrayMenuWindow().Attach(_engine, _openMain, _quit, anchor);
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
        _engine.Changed -= OnEngineChanged;
        _tooltipTimer.Stop();
        _pendingOpen?.Stop();
        _menu?.Close();
        if (_tray is not null)
        {
            // Removes the icon from the notification area right away instead of leaving a ghost.
            _tray.IsVisible = false;
            _tray.Dispose();
            _tray = null;
        }
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
