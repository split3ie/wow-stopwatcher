using Avalonia.Controls;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Tray;

/// <summary>
/// The tray menu on macOS and Linux: a native menu with the stopwatches (checked while running), "Next stopwatch",
/// Stop all, Open and Quit. The menu is rebuilt when the stopwatches change and its items are updated in place.
/// </summary>
internal sealed class NativeTrayMenu : ITrayMenu
{
    private readonly IStopwatchControl _control;
    private readonly IMainWindowService _mainWindow;
    private readonly IAppLifetime _lifetime;
    private readonly NativeMenu _menu = new();
    private readonly List<(string Id, NativeMenuItem Item)> _stopwatchItems = [];
    private string? _menuSignature;

    public NativeTrayMenu(IStopwatchControl control, IMainWindowService mainWindow, IAppLifetime lifetime)
    {
        _control = control;
        _mainWindow = mainWindow;
        _lifetime = lifetime;
    }

    public void Attach(TrayIcon tray) => tray.Menu = _menu;

    public void Update(StopwatchEngineSnapshot snapshot)
    {
        var signature = string.Join('|', snapshot.Stopwatches.Select(s => $"{s.Id}:{s.Name}:{s.Color}:{s.IsIndependent}"));
        if (signature != _menuSignature)
        {
            _menuSignature = signature;
            Rebuild(snapshot);
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

            var isRunning = snapshot.IsRunning(stopwatchId);
            if (item.IsChecked != isRunning)
            {
                item.IsChecked = isRunning;
            }

            // A running stopwatch stays stoppable; a stopped one cannot start past the concurrent limit.
            var canToggle = isRunning || snapshot.CanStart(stopwatchId);
            if (item.IsEnabled != canToggle)
            {
                item.IsEnabled = canToggle;
            }
        }
    }

    private void Rebuild(StopwatchEngineSnapshot snapshot)
    {
        var menu = _menu;
        menu.Items.Clear();
        _stopwatchItems.Clear();

        foreach (var stopwatch in snapshot.Stopwatches)
        {
            var id = stopwatch.Id;
            var item = new NativeMenuItem(stopwatch.Name) { ToggleType = MenuItemToggleType.CheckBox };
            item.Click += (_, _) => _control.Toggle(id);
            _stopwatchItems.Add((id, item));
            menu.Items.Add(item);
        }

        if (snapshot.Stopwatches.Count > 0)
        {
            menu.Items.Add(new NativeMenuItemSeparator());
        }

        var next = new NativeMenuItem(Loc.T("tray.next"));
        next.Click += (_, _) => _control.StartNext();
        var stopAll = new NativeMenuItem(Loc.T("tray.stopAll"));
        stopAll.Click += (_, _) => _control.StopAll();
        var open = new NativeMenuItem(Loc.T("tray.open"));
        open.Click += (_, _) => _mainWindow.Show();
        var quit = new NativeMenuItem(Loc.T("tray.quit"));
        quit.Click += (_, _) => _lifetime.Quit();

        menu.Items.Add(next);
        menu.Items.Add(stopAll);
        menu.Items.Add(open);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);
    }

    public void Dispose()
    {
    }
}
