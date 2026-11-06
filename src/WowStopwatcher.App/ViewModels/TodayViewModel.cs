using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.ViewModels;

/// <summary>The Today screen: timeline, stopwatch rows, total and goals.</summary>
public sealed partial class TodayViewModel : ObservableObject
{
    private const int GoalLimit = 4;
    private static readonly TimeSpan VisibleInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan HiddenInterval = TimeSpan.FromSeconds(1);

    private readonly StopwatchEngine _engine;
    private readonly IStopwatchRepository _stopwatches;
    private readonly ConfirmDialogViewModel _dialog;
    private readonly Action _openSettings;
    private readonly DispatcherTimer _timer;

    private bool _isPageActive = true;
    private bool _showAllGoals;
    private string? _runningColor;
    private string? _startColor;
    private Func<StopwatchEngineSnapshot, (string Body, string Label)>? _confirmText;

    public TodayViewModel(
        StopwatchEngine engine,
        IStopwatchRepository stopwatches,
        ConfirmDialogViewModel dialog,
        Action openSettings)
    {
        _engine = engine;
        _stopwatches = stopwatches;
        _dialog = dialog;
        _openSettings = openSettings;

        _engine.Changed += OnEngineChanged;
        _timer = new DispatcherTimer(VisibleInterval, DispatcherPriority.Normal, OnTimerTick);
        _timer.Start();
        Refresh();
    }

    public TimelineViewModel Timeline { get; } = new();

    public ObservableCollection<StopwatchRowViewModel> Rows { get; } = [];

    /// <summary>The goals shown in the Goals card (limited unless expanded).</summary>
    public ObservableCollection<GoalItemViewModel> Goals { get; } = [];

    [ObservableProperty]
    private bool _hasStopwatches;

    [ObservableProperty]
    private string _totalText = "00:00:00";

    [ObservableProperty]
    private string _totalHuman = string.Empty;

    [ObservableProperty]
    private bool _isAnyRunning;

    [ObservableProperty]
    private string _runningText = "Nothing is running";

    [ObservableProperty]
    private string _runningElapsedText = string.Empty;

    /// <summary>Color of the running stopwatch; null when nothing runs.</summary>
    [ObservableProperty]
    private IBrush? _runningBrush;

    [ObservableProperty]
    private string _mainActionLabel = "Start";

    [ObservableProperty]
    private string _startCaption = string.Empty;

    /// <summary>Color of the stopwatch the Start button would start; null when there is none.</summary>
    [ObservableProperty]
    private IBrush? _startBrush;

    [ObservableProperty]
    private string _goalsSummary = "None set";

    [ObservableProperty]
    private bool _canToggleGoals;

    [ObservableProperty]
    private string _toggleGoalsLabel = string.Empty;

    [ObservableProperty]
    private bool _hasReachedGoals;

    [ObservableProperty]
    private string _reachedNames = string.Empty;

    /// <summary>Called by the main window: refresh quickly while visible, once a second when hidden or minimized.</summary>
    public void SetWindowVisible(bool visible) =>
        _timer.Interval = visible ? VisibleInterval : HiddenInterval;

    /// <summary>Called by navigation: rebuilds immediately when the page becomes visible again.</summary>
    public void SetPageActive(bool active)
    {
        _isPageActive = active;
        if (active)
        {
            Refresh();
        }
    }

    internal void Toggle(string id) => _engine.Toggle(id);

    internal void RequestReset(StopwatchRowViewModel row)
    {
        var id = row.Id;
        _confirmText = snapshot =>
        {
            var definition = snapshot.Find(id);
            var name = definition?.Name ?? row.Name;
            var sessions = snapshot.GetTodaySessions(id).Count;
            var running = snapshot.IsRunning(id);
            var body = $"This deletes {sessions} {(sessions == 1 ? "session" : "sessions")} logged today "
                + $"({TimeFormat.Human(snapshot.GetTodayTotal(id))})"
                + (running ? " and stops the stopwatch" : string.Empty)
                + ". Other stopwatches aren’t affected. This can’t be undone.";
            return (body, "Reset " + name);
        };

        var (body, label) = _confirmText(_engine.GetSnapshot());
        _dialog.Show($"Reset {row.Name}?", body, label, () => _engine.ResetToday(id));
    }

    /// <summary>Moves a row by one position (keyboard reordering) and saves the new order.</summary>
    internal void Move(string id, int delta)
    {
        var ids = Rows.Select(r => r.Id).ToList();
        var from = ids.IndexOf(id);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= ids.Count)
        {
            return;
        }

        ids.RemoveAt(from);
        ids.Insert(to, id);
        SaveOrder(ids);
    }

    /// <summary>Moves a dragged row to the position of the target row and saves the new order.</summary>
    public void MoveTo(string id, string targetId)
    {
        var ids = Rows.Select(r => r.Id).ToList();
        var from = ids.IndexOf(id);
        var to = ids.IndexOf(targetId);
        if (from < 0 || to < 0 || from == to)
        {
            return;
        }

        ids.RemoveAt(from);
        ids.Insert(to, id);
        SaveOrder(ids);
    }

    private void SaveOrder(List<string> visibleIds)
    {
        // Deleted stopwatches keep a slot after the visible ones so sort orders stay unique.
        var deleted = _engine.GetSnapshot().AllStopwatches.Where(s => s.IsDeleted).Select(s => s.Id);
        _stopwatches.SaveSortOrder([.. visibleIds, .. deleted]);
        _engine.ReloadDefinitions();
    }

    [RelayCommand]
    private void MainAction()
    {
        if (IsAnyRunning)
        {
            _engine.StopAll();
        }
        else
        {
            _engine.StartNext();
        }
    }

    [RelayCommand]
    private void ResetAll()
    {
        _confirmText = snapshot =>
        {
            var sessions = snapshot.TodaySessions.Count;
            var running = snapshot.RunningId is { } id ? snapshot.Find(id)?.Name : null;
            var what = sessions == 1 ? "the 1 session" : $"all {sessions} sessions";
            var body = $"This deletes {what} logged today ({TimeFormat.Human(snapshot.GrandTotal)})"
                + (running is not null ? $" and stops {running}" : string.Empty)
                + ". This can’t be undone.";
            return (body, "Reset all");
        };

        var (body, label) = _confirmText(_engine.GetSnapshot());
        _dialog.Show("Reset all stopwatches?", body, label, _engine.ResetAllToday);
    }

    [RelayCommand]
    private void ToggleGoals()
    {
        _showAllGoals = !_showAllGoals;
        Refresh();
    }

    [RelayCommand]
    private void OpenSettings() => _openSettings();

    private void OnEngineChanged(object? sender, EventArgs e)
    {
        // Changed is raised on the calling thread, which may be a hotkey or tray thread.
        if (Dispatcher.UIThread.CheckAccess())
        {
            Refresh();
        }
        else
        {
            Dispatcher.UIThread.Post(Refresh);
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _engine.Tick();
        Refresh();
    }

    private void Refresh()
    {
        if (!_isPageActive)
        {
            return;
        }

        var snapshot = _engine.GetSnapshot();
        var timeline = TodayTimelineBuilder.Build(snapshot);

        UpdateRows(snapshot);
        Timeline.Update(snapshot, timeline);
        UpdateSummary(snapshot);
        UpdateGoals(snapshot);

        if (_dialog.IsOpen && _confirmText is not null)
        {
            _dialog.Body = _confirmText(snapshot).Body;
        }
    }

    private void UpdateRows(StopwatchEngineSnapshot snapshot)
    {
        var definitions = snapshot.Stopwatches;
        HasStopwatches = definitions.Count > 0;

        // Reconcile in place: keep existing row view models (and their controls), reorder, add, remove.
        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            var current = -1;
            for (var j = i; j < Rows.Count; j++)
            {
                if (Rows[j].Id == definition.Id)
                {
                    current = j;
                    break;
                }
            }

            if (current < 0)
            {
                Rows.Insert(i, new StopwatchRowViewModel(this, definition.Id));
            }
            else if (current != i)
            {
                Rows.Move(current, i);
            }

            Rows[i].Update(snapshot, definition);
        }

        while (Rows.Count > definitions.Count)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }
    }

    private void UpdateSummary(StopwatchEngineSnapshot snapshot)
    {
        var count = snapshot.Stopwatches.Count;
        TotalText = TimeFormat.Clock(snapshot.GrandTotal);
        TotalHuman = $"{TimeFormat.Human(snapshot.GrandTotal)} across {count} {(count == 1 ? "stopwatch" : "stopwatches")}";

        var running = snapshot.RunningId is { } runningId ? snapshot.Find(runningId) : null;
        IsAnyRunning = running is not null;
        RunningText = running is null
            ? "Nothing is running"
            : $"{running.Name} is running";
        RunningElapsedText = running is null ? string.Empty : TimeFormat.Clock(snapshot.RunningElapsed);
        if (_runningColor != running?.Color)
        {
            _runningColor = running?.Color;
            RunningBrush = _runningColor is null ? null : Brush.Parse(_runningColor);
        }

        MainActionLabel = running is null ? "Start" : "Stop";

        // Same choice as StopwatchEngine.StartNext when nothing runs: the last used stopwatch, else the first.
        var lastUsed = snapshot.LastUsedId is { } last ? snapshot.Stopwatches.FirstOrDefault(s => s.Id == last) : null;
        var target = lastUsed ?? snapshot.Stopwatches.FirstOrDefault();
        StartCaption = target is null
            ? "No stopwatches yet"
            : $"Starts {target.Name} ({(lastUsed is not null ? "last used" : "first in list")})";
        if (_startColor != target?.Color)
        {
            _startColor = target?.Color;
            StartBrush = _startColor is null ? null : Brush.Parse(_startColor);
        }
    }

    private void UpdateGoals(StopwatchEngineSnapshot snapshot)
    {
        var withGoal = snapshot.Stopwatches.Where(s => s.HasGoal).ToList();
        var inProgress = new List<(StopwatchDefinition Definition, TimeSpan Total)>();
        var reached = new List<string>();
        foreach (var definition in withGoal)
        {
            var total = snapshot.GetTodayTotal(definition.Id);
            if (total >= definition.Goal!.Value)
            {
                reached.Add(definition.Name);
            }
            else
            {
                inProgress.Add((definition, total));
            }
        }

        inProgress = inProgress
            .OrderByDescending(g => g.Total / g.Definition.Goal!.Value)
            .ToList();
        var shown = _showAllGoals ? inProgress : inProgress.Take(GoalLimit).ToList();

        CollectionSync.Resize(Goals, shown.Count, () => new GoalItemViewModel());
        for (var i = 0; i < shown.Count; i++)
        {
            Goals[i].Update(shown[i].Definition, shown[i].Total);
        }

        GoalsSummary = withGoal.Count > 0 ? $"{reached.Count} of {withGoal.Count} reached" : "None set";
        CanToggleGoals = inProgress.Count > GoalLimit;
        ToggleGoalsLabel = _showAllGoals ? "Show fewer" : $"Show {inProgress.Count - GoalLimit} more";
        HasReachedGoals = reached.Count > 0;
        ReachedNames = string.Join(", ", reached);
    }
}
