using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Navigation;
using WowStopwatcher.App.Shell;
using WowStopwatcher.App.ViewModels;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Today;

/// <summary>The Today screen: timeline, stopwatch rows, total and goals.</summary>
public sealed partial class TodayViewModel : ObservableObject, INavigationAware, IWindowVisibilityAware
{
    private const int GoalLimit = 4;
    private static readonly TimeSpan VisibleInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan HiddenInterval = TimeSpan.FromSeconds(1);

    private readonly IStopwatchState _state;
    private readonly IStopwatchControl _control;
    private readonly IStopwatchMaintenance _maintenance;
    private readonly IStopwatchRepository _stopwatches;
    private readonly IDialogService _dialog;
    private readonly INavigationService _navigation;
    private readonly StopwatchCapabilities _capabilities;
    private readonly DispatcherTimer _timer;

    private bool _isPageActive = true;
    private bool _showAllGoals;
    private string? _runningColor;
    private string? _startColor;
    private Func<StopwatchEngineSnapshot, (string Body, string Label)>? _confirmText;

    public TodayViewModel(
        IStopwatchState state,
        IStopwatchControl control,
        IStopwatchMaintenance maintenance,
        IStopwatchRepository stopwatches,
        IDialogService dialog,
        INavigationService navigation,
        StopwatchCapabilities capabilities)
    {
        _state = state;
        _control = control;
        _maintenance = maintenance;
        _stopwatches = stopwatches;
        _dialog = dialog;
        _navigation = navigation;
        _capabilities = capabilities;

        _state.Changed += OnEngineChanged;
        _timer = new DispatcherTimer(VisibleInterval, DispatcherPriority.Normal, OnTimerTick);
        _timer.Start();
        Refresh();
    }

    /// <summary>False when the build has no stopwatch goals; the Goals card is then hidden.</summary>
    public bool AreGoalsAvailable => _capabilities.Goals;

    /// <summary>False when the build has no Settings page; the empty state then has no link to it.</summary>
    public bool CanOpenSettings => _navigation.CanNavigateTo(PageKeys.Settings);

    /// <summary>Text of the empty state; it points to Settings only when that page exists.</summary>
    public string EmptyHint => CanOpenSettings
        ? Loc.T("today.empty.hintSettings")
        : Loc.T("today.empty.hintNone");

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
    private string _runningText = Loc.T("today.running.nothing");

    [ObservableProperty]
    private string _runningElapsedText = string.Empty;

    /// <summary>Color of the running stopwatch; null when nothing runs.</summary>
    [ObservableProperty]
    private IBrush? _runningBrush;

    [ObservableProperty]
    private string _mainActionLabel = Loc.T("today.start");

    [ObservableProperty]
    private string _startCaption = string.Empty;

    /// <summary>Color of the stopwatch the Start button would start; null when there is none.</summary>
    [ObservableProperty]
    private IBrush? _startBrush;

    [ObservableProperty]
    private string _goalsSummary = Loc.T("today.goals.none");

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

    public void OnNavigatedTo() => SetPageActive(true);

    public void OnNavigatedFrom() => SetPageActive(false);

    /// <summary>Rebuilds immediately when the page becomes visible again.</summary>
    public void SetPageActive(bool active)
    {
        _isPageActive = active;
        if (active)
        {
            Refresh();
        }
    }

    internal void Toggle(string id) => _control.Toggle(id);

    internal void RequestReset(StopwatchRowViewModel row)
    {
        var id = row.Id;
        _confirmText = snapshot =>
        {
            var definition = snapshot.Find(id);
            var name = definition?.Name ?? row.Name;
            var sessions = snapshot.GetTodaySessions(id).Count;
            var running = snapshot.IsRunning(id);
            var body = Loc.Plural(
                running ? "today.reset.bodyRunning" : "today.reset.body",
                sessions,
                ("time", TimeFormat.Human(snapshot.GetTodayTotal(id))));
            return (body, Loc.T("today.reset.confirm", ("name", name)));
        };

        var (body, label) = _confirmText(_state.GetSnapshot());
        _dialog.Show(Loc.T("today.reset.title", ("name", row.Name)), body, label, () => _maintenance.ResetToday(id));
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
        var deleted = _state.GetSnapshot().AllStopwatches.Where(s => s.IsDeleted).Select(s => s.Id);
        _stopwatches.SaveSortOrder([.. visibleIds, .. deleted]);
        _maintenance.ReloadDefinitions();
    }

    [RelayCommand]
    private void MainAction()
    {
        if (IsAnyRunning)
        {
            _control.StopAll();
        }
        else
        {
            _control.StartNext();
        }
    }

    [RelayCommand]
    private void ResetAll()
    {
        _confirmText = snapshot =>
        {
            var sessions = snapshot.TodaySessions.Count;
            var running = snapshot.IsAnyRunning
                ? string.Join(", ", snapshot.RunningSessions.Select(s => snapshot.Find(s.StopwatchId)?.Name).Where(n => n is not null))
                : null;
            var body = running is not null
                ? Loc.Plural("today.resetAll.bodyRunning", sessions, ("time", TimeFormat.Human(snapshot.GrandTotal)), ("names", running))
                : Loc.Plural("today.resetAll.body", sessions, ("time", TimeFormat.Human(snapshot.GrandTotal)));
            return (body, Loc.T("today.resetAll"));
        };

        var (body, label) = _confirmText(_state.GetSnapshot());
        _dialog.Show(Loc.T("today.resetAll.title"), body, label, _maintenance.ResetAllToday);
    }

    [RelayCommand]
    private void ToggleGoals()
    {
        _showAllGoals = !_showAllGoals;
        Refresh();
    }

    [RelayCommand]
    private void OpenSettings() => _navigation.NavigateTo(PageKeys.Settings);

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
        _maintenance.Tick();
        Refresh();
    }

    private void Refresh()
    {
        if (!_isPageActive)
        {
            return;
        }

        var snapshot = _state.GetSnapshot();
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
        TotalHuman = Loc.Plural("today.totalAcross", count, ("time", TimeFormat.Human(snapshot.GrandTotal)));

        // Shows the running normal stopwatch; when only independent ones run, the first of them.
        var runningSession = snapshot.RunningSession ?? snapshot.RunningSessions.FirstOrDefault();
        var running = runningSession is null ? null : snapshot.Find(runningSession.StopwatchId);
        IsAnyRunning = snapshot.IsAnyRunning;
        RunningText = running is null
            ? Loc.T("today.running.nothing")
            : Loc.T("today.running.name", ("name", running.Name));
        RunningElapsedText = runningSession is null ? string.Empty : TimeFormat.Clock(snapshot.GetDuration(runningSession));
        if (_runningColor != running?.Color)
        {
            _runningColor = running?.Color;
            RunningBrush = _runningColor is null ? null : Brush.Parse(_runningColor);
        }

        MainActionLabel = snapshot.IsAnyRunning ? Loc.T("today.stop") : Loc.T("today.start");

        // Same choice as StopwatchEngine.StartNext when nothing runs: the last used stopwatch, else the first.
        var lastUsed = snapshot.LastUsedId is { } last ? snapshot.Stopwatches.FirstOrDefault(s => s.Id == last) : null;
        var target = lastUsed ?? snapshot.Stopwatches.FirstOrDefault();
        StartCaption = target is null
            ? Loc.T("today.empty.title")
            : Loc.T(lastUsed is not null ? "today.startCaption.lastUsed" : "today.startCaption.first", ("name", target.Name));
        if (_startColor != target?.Color)
        {
            _startColor = target?.Color;
            StartBrush = _startColor is null ? null : Brush.Parse(_startColor);
        }
    }

    private void UpdateGoals(StopwatchEngineSnapshot snapshot)
    {
        var withGoal = snapshot.Stopwatches.Where(s => s.HasGoal).ToList();
        var inProgress = new List<(StopwatchDefinition Definition, GoalStatus Status)>();
        var reached = new List<string>();
        foreach (var definition in withGoal)
        {
            if (snapshot.GetGoalStatus(definition.Id) is not { } status)
            {
                continue;
            }

            if (status.IsReached)
            {
                reached.Add(definition.Name);
            }
            else
            {
                inProgress.Add((definition, status));
            }
        }

        inProgress = inProgress
            .OrderByDescending(g => g.Status.Progress / g.Status.Goal)
            .ToList();
        var shown = _showAllGoals ? inProgress : inProgress.Take(GoalLimit).ToList();

        CollectionSync.Resize(Goals, shown.Count, () => new GoalItemViewModel());
        for (var i = 0; i < shown.Count; i++)
        {
            Goals[i].Update(shown[i].Definition, shown[i].Status, snapshot.TimeZone);
        }

        GoalsSummary = withGoal.Count > 0
            ? Loc.T("today.goals.summary", ("reached", reached.Count), ("total", withGoal.Count))
            : Loc.T("today.goals.none");
        CanToggleGoals = inProgress.Count > GoalLimit;
        ToggleGoalsLabel = _showAllGoals ? Loc.T("today.goals.showFewer") : Loc.T("today.goals.showMore", ("count", inProgress.Count - GoalLimit));
        HasReachedGoals = reached.Count > 0;
        ReachedNames = string.Join(", ", reached);
    }
}
