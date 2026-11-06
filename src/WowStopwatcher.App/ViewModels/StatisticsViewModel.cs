using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.ViewModels;

/// <summary>The Statistics page: one card per logged day. Created fresh each time the page is opened.</summary>
public sealed partial class StatisticsViewModel : ObservableObject
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private readonly StopwatchEngine _engine;
    private readonly IStopwatchRepository _stopwatches;
    private readonly ISessionRepository _sessions;
    private readonly TimeProvider _time;
    private readonly ConfirmDialogViewModel _dialog;
    private readonly DispatcherTimer _timer;
    private bool _expandedToday;

    public StatisticsViewModel(
        StopwatchEngine engine,
        IStopwatchRepository stopwatches,
        ISessionRepository sessions,
        TimeProvider time,
        ConfirmDialogViewModel dialog)
    {
        _engine = engine;
        _stopwatches = stopwatches;
        _sessions = sessions;
        _time = time;
        _dialog = dialog;

        _timer = new DispatcherTimer(RefreshInterval, DispatcherPriority.Normal, (_, _) => Reload());
        Reload();
    }

    public ObservableCollection<DayViewModel> Days { get; } = [];

    public ObservableCollection<LegendItemViewModel> Legend { get; } = [];

    [ObservableProperty]
    private bool _hasDays;

    [ObservableProperty]
    private bool _isSelecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private int _selectedCount;

    public bool HasSelection => SelectedCount > 0;

    [ObservableProperty]
    private string _selectedText = "0 selected";

    /// <summary>Called by the view while it is on screen: refreshes every second so today's bar grows.</summary>
    public void Start()
    {
        Reload();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    internal void OnSelectionChanged()
    {
        SelectedCount = Days.Count(d => d.IsSelected);
        SelectedText = $"{SelectedCount} selected";
    }

    [RelayCommand]
    private void StartSelect() => IsSelecting = true;

    [RelayCommand]
    private void CancelSelect() => EndSelect();

    [RelayCommand]
    private void ClearAll()
    {
        var count = Days.Count;
        if (count == 0)
        {
            return;
        }

        _dialog.Show(
            "Clear all statistics?",
            $"This deletes every logged session ({Plural(count)}). This can’t be undone.",
            "Clear all",
            () =>
            {
                _engine.ResetAllToday();
                _sessions.DeleteAll();
                EndSelect();
                Reload();
            });
    }

    [RelayCommand]
    private void ClearSelected()
    {
        var selected = Days.Where(d => d.IsSelected).Select(d => d.Date).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var snapshot = _engine.GetSnapshot();
        var includesToday = selected.Contains(snapshot.Today);
        var stopsRunning = includesToday && snapshot.RunningId is not null;
        var body = $"This deletes every logged session from the {(selected.Count == 1 ? "selected day" : $"{selected.Count} selected days")}"
            + (stopsRunning ? " and stops the running stopwatch" : string.Empty)
            + ". This can’t be undone.";

        _dialog.Show($"Clear {Plural(selected.Count)}?", body, $"Clear {Plural(selected.Count)}", () =>
        {
            var zone = _time.LocalTimeZone;
            var today = _engine.GetSnapshot().Today;
            if (selected.Contains(today))
            {
                // The engine owns today's sessions and its cache, so it stops the running stopwatch and deletes them.
                _engine.ResetAllToday();
            }

            var others = selected.Where(d => d != today).ToList();
            if (others.Count > 0)
            {
                _sessions.DeleteForLocalDays(others, zone);
            }

            EndSelect();
            Reload();
        });
    }

    private static string Plural(int days) => days == 1 ? "1 day" : $"{days} days";

    private void EndSelect()
    {
        foreach (var day in Days)
        {
            day.IsSelected = false;
        }

        IsSelecting = false;
    }

    private void Reload()
    {
        var zone = _time.LocalTimeZone;
        var definitions = _stopwatches.GetAll(includeDeleted: true);

        IReadOnlyList<Session> sessions = [];
        var localDays = _sessions.GetLocalDays(zone);
        if (localDays.Count > 0)
        {
            var from = LocalDays.DayRangeUtc(localDays[^1], zone).StartUtc;
            var to = LocalDays.DayRangeUtc(localDays[0], zone).EndUtc;
            sessions = _sessions.GetInRange(from, to);
        }

        var result = DailyStatisticsBuilder.Build(sessions, definitions, _time.GetUtcNow(), zone);
        var today = _engine.GetSnapshot().Today;
        var names = definitions.ToDictionary(d => d.Id, d => d.Name);

        UpdateLegend(definitions);
        UpdateDays(result, today, names);
        HasDays = Days.Count > 0;
        OnSelectionChanged();
    }

    private void UpdateLegend(IReadOnlyList<StopwatchDefinition> definitions)
    {
        var active = definitions.Where(d => !d.IsDeleted).ToList();
        CollectionSync.Resize(Legend, active.Count, () => new LegendItemViewModel());
        for (var i = 0; i < active.Count; i++)
        {
            Legend[i].Update(active[i]);
            if (active[i].HasGoal)
            {
                Legend[i].Name += " · goal " + TimeFormat.GoalLabel(active[i].Goal!.Value);
            }
        }
    }

    private void UpdateDays(StatisticsResult result, DateOnly today, IReadOnlyDictionary<string, string> names)
    {
        for (var i = 0; i < result.Days.Count; i++)
        {
            var stats = result.Days[i];
            var current = -1;
            for (var j = i; j < Days.Count; j++)
            {
                if (Days[j].Date == stats.Date)
                {
                    current = j;
                    break;
                }
            }

            if (current < 0)
            {
                Days.Insert(i, new DayViewModel(this, stats.Date));
            }
            else if (current != i)
            {
                Days.Move(current, i);
            }

            Days[i].Update(stats, result, today, names);
        }

        while (Days.Count > result.Days.Count)
        {
            Days.RemoveAt(Days.Count - 1);
        }

        // Today's card starts expanded; after that the user's choice wins.
        if (!_expandedToday)
        {
            _expandedToday = true;
            Days.FirstOrDefault(d => d.Date == today)?.Toggle();
        }
    }
}
