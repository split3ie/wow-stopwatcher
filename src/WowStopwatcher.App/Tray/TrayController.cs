using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.Tray;

/// <summary>
/// Owns the tray icon: tooltip and icon color. On Windows: single click opens a custom menu, double click starts the
/// next stopwatch. On macOS and Linux: a native menu with the stopwatches, "Next stopwatch", Stop all, Open and Quit.
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
    private NativeMenu? _stopwatchMenu;
    private string? _menuSignature;
    private readonly List<(string Id, NativeMenuItem Item)> _stopwatchItems = [];

    public TrayController(StopwatchEngine engine, Action openMain, Action quit)
    {
        _engine = engine;
        _openMain = openMain;
        _quit = quit;
    }

    public void Start()
    {
        // Windows right click: a plain native menu (left click is the custom menu).
        var open = new NativeMenuItem("Open WowStopwatcher");
        open.Click += (_, _) => _openMain();
        var stopAll = new NativeMenuItem("Stop all");
        stopAll.Click += (_, _) => _engine.StopAll();
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => _quit();

        _tray = new TrayIcon
        {
            ToolTipText = "WowStopwatcher",
            IsVisible = true,
        };
        if (OperatingSystem.IsWindows())
        {
            _tray.Menu = [open, stopAll, new NativeMenuItemSeparator(), quit];
            _tray.Clicked += OnClicked;
        }
        else
        {
            _stopwatchMenu = new NativeMenu();
            _tray.Menu = _stopwatchMenu;
        }

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

        if (_stopwatchMenu is not null)
        {
            UpdateNativeMenu(snapshot);
        }

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

    private void UpdateNativeMenu(StopwatchEngineSnapshot snapshot)
    {
        var signature = string.Join('|', snapshot.Stopwatches.Select(s => $"{s.Id}:{s.Name}:{s.Color}"));
        if (signature != _menuSignature)
        {
            _menuSignature = signature;
            RebuildNativeMenu(snapshot);
        }

        foreach (var (stopwatchId, item) in _stopwatchItems)
        {
            var definition = snapshot.Find(stopwatchId);
            if (definition is null)
            {
                continue;
            }

            var header = $"{definition.Name}  {TimeFormat.Clock(snapshot.GetTodayTotal(stopwatchId))}";
            if (item.Header != header)
            {
                item.Header = header;
            }

            var isRunning = snapshot.RunningId == stopwatchId;
            if (item.IsChecked != isRunning)
            {
                item.IsChecked = isRunning;
            }
        }
    }

    private void RebuildNativeMenu(StopwatchEngineSnapshot snapshot)
    {
        var menu = _stopwatchMenu!;
        menu.Items.Clear();
        _stopwatchItems.Clear();

        foreach (var stopwatch in snapshot.Stopwatches)
        {
            var id = stopwatch.Id;
            var item = new NativeMenuItem(stopwatch.Name) { ToggleType = MenuItemToggleType.CheckBox };
            item.Click += (_, _) => _engine.Toggle(id);
            _stopwatchItems.Add((id, item));
            menu.Items.Add(item);
        }

        if (snapshot.Stopwatches.Count > 0)
        {
            menu.Items.Add(new NativeMenuItemSeparator());
        }

        var next = new NativeMenuItem("Next stopwatch");
        next.Click += (_, _) => _engine.StartNext();
        var stopAll = new NativeMenuItem("Stop all");
        stopAll.Click += (_, _) => _engine.StopAll();
        var open = new NativeMenuItem("Open WowStopwatcher");
        open.Click += (_, _) => _openMain();
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => _quit();

        menu.Items.Add(next);
        menu.Items.Add(stopAll);
        menu.Items.Add(open);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);
    }

    [SupportedOSPlatform("windows")]
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

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [SupportedOSPlatform("windows")]
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
