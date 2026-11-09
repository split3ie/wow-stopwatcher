using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using WowStopwatcher.App.Composition;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.Features.Tray;

/// <summary>
/// Owns the tray icon: tooltip and icon color. The menu and click behavior come from the <see cref="ITrayMenu"/> of the
/// current operating system. Must be created and used on the UI thread.
/// </summary>
internal sealed class TrayController : IAppService
{
    private readonly IStopwatchState _state;
    private readonly IUiDispatcher _dispatcher;
    private readonly ITrayMenu _menu;
    private readonly DispatcherTimer _tooltipTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private TrayIcon? _tray;
    private string? _iconKey;

    public TrayController(IStopwatchState state, IUiDispatcher dispatcher, ITrayMenu menu)
    {
        _state = state;
        _dispatcher = dispatcher;
        _menu = menu;
    }

    public void Start()
    {
        _tray = new TrayIcon
        {
            ToolTipText = "WowStopwatcher",
            IsVisible = true,
        };
        _menu.Attach(_tray);

        TrayIcon.SetIcons(Application.Current!, [_tray]);

        UpdateIconAndTooltip();
        _tooltipTimer.Tick += (_, _) => UpdateIconAndTooltip();
        _tooltipTimer.Start();
        _state.Changed += OnEngineChanged;
    }

    private void OnEngineChanged(object? sender, EventArgs e) => _dispatcher.Post(UpdateIconAndTooltip);

    private void UpdateIconAndTooltip()
    {
        if (_tray is null)
        {
            return;
        }

        var snapshot = _state.GetSnapshot();
        // The running normal stopwatch; when only independent ones run, the first of them.
        var runningSession = snapshot.RunningSession ?? snapshot.RunningSessions.FirstOrDefault();
        var running = runningSession is null ? null : snapshot.Find(runningSession.StopwatchId);

        _menu.Update(snapshot);

        // One line per running stopwatch with its total for today, the normal one first.
        var lines = snapshot.RunningSessions
            .OrderBy(s => s == snapshot.RunningSession ? 0 : 1)
            .Select(s => (Session: s, Definition: snapshot.Find(s.StopwatchId)))
            .Where(x => x.Definition is not null)
            .Select(x => $"{x.Definition!.Name} {TimeFormat.Clock(snapshot.GetTodayTotal(x.Session.StopwatchId))}");
        var tooltip = string.Join(Environment.NewLine, lines.Prepend("WowStopwatcher"));
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

    public void Dispose()
    {
        _state.Changed -= OnEngineChanged;
        _tooltipTimer.Stop();
        _menu.Dispose();
        if (_tray is not null)
        {
            // Removes the icon from the notification area right away instead of leaving a ghost.
            _tray.IsVisible = false;
            _tray.Dispose();
            _tray = null;
        }
    }
}
