using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.ViewModels;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Statistics;

/// <summary>A stopwatch's total on one day, shown in the expanded details.</summary>
public sealed partial class DayStopwatchRowViewModel : ObservableObject
{
    private readonly StatisticsViewModel _owner;
    private string? _color;
    private (string StopwatchId, string Name, DateOnly Day)? _actionsFor;

    public DayStopwatchRowViewModel(StatisticsViewModel owner) => _owner = owner;

    /// <summary>Id of the stopwatch this row belongs to.</summary>
    public string StopwatchId { get; private set; } = string.Empty;

    /// <summary>True for a hidden (deleted or expired) stopwatch, which can be restored.</summary>
    [ObservableProperty]
    private bool _isDeleted;

    /// <summary>True while the pointer is over the row or keyboard focus is inside it (shows the Restore button).</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Buttons contributed by features (for example Invoice) for this stopwatch and day.</summary>
    public ObservableCollection<DayStopwatchActionViewModel> Actions { get; } = [];

    /// <summary>Width of the actions column: reserved for every row of a day when any row has an action, so the bars stay comparable.</summary>
    [ObservableProperty]
    private GridLength _actionsWidth = new(0);

    public bool HasActions => Actions.Count > 0;

    public string RestoreLabel => Loc.T("stats.restore.label", ("name", Name));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RestoreLabel))]
    private string _name = string.Empty;

    [ObservableProperty]
    private IBrush? _brush;

    [ObservableProperty]
    private bool _isReached;

    [ObservableProperty]
    private string _timeText = string.Empty;

    /// <summary>Secondary note next to the time ("×2", "reached Mon 20:00", "3h 00m so far"); empty when not needed.</summary>
    [ObservableProperty]
    private string _noteText = string.Empty;

    [ObservableProperty]
    private GridLength _shareWidth = new(0, GridUnitType.Star);

    [ObservableProperty]
    private GridLength _overtimeWidth = new(0, GridUnitType.Star);

    [ObservableProperty]
    private GridLength _restWidth = new(1, GridUnitType.Star);

    [RelayCommand]
    private void Restore() => _owner.Restore(StopwatchId);

    public void Update(DayStopwatchTotal total, DateOnly day, TimeZoneInfo zone)
    {
        var share = Math.Clamp(total.Share, 0, 1);
        StopwatchId = total.StopwatchId;
        IsDeleted = total.IsDeleted;
        Name = total.Name;
        IsReached = total.GoalReached;
        TimeText = TimeFormat.HoursMinutes(total.Total)
            + (total.Goal is { } goal ? " / " + TimeFormat.GoalLabel(goal) : string.Empty);
        NoteText = BuildNote(total, day, zone);
        UpdateActions(total, day);
        var overtime = Math.Clamp(total.OvertimeShare, 0, share);
        ShareWidth = new GridLength(share - overtime, GridUnitType.Star);
        OvertimeWidth = new GridLength(overtime, GridUnitType.Star);
        RestWidth = new GridLength(1 - share, GridUnitType.Star);
        if (_color != total.Color)
        {
            _color = total.Color;
            Brush = Avalonia.Media.Brush.Parse(total.Color);
        }
    }

    private void UpdateActions(DayStopwatchTotal total, DateOnly day)
    {
        var key = (total.StopwatchId, total.Name, day);
        if (_actionsFor == key)
        {
            return;
        }

        _actionsFor = key;
        Actions.Clear();
        foreach (var action in _owner.ActionsFor(total.StopwatchId))
        {
            Actions.Add(new DayStopwatchActionViewModel(action, total.StopwatchId, day, Loc.T("stats.action.label", ("action", action.Label), ("name", total.Name))));
        }

        OnPropertyChanged(nameof(HasActions));
    }

    private static string BuildNote(DayStopwatchTotal total, DateOnly day, TimeZoneInfo zone)
    {
        var parts = new List<string>();
        if (total.GoalsReachedCount > 1)
        {
            parts.Add(Loc.T("stats.note.timesReached", ("count", total.GoalsReachedCount)));
        }

        if (total.GoalReachedUtc is { } reachedUtc)
        {
            var local = TimeZoneInfo.ConvertTime(reachedUtc, zone);
            if (DateOnly.FromDateTime(local.DateTime) != day)
            {
                parts.Add(Loc.T("stats.note.reachedAt", ("time", local.ToString(Loc.T("stats.format.weekdayTime"), Loc.Culture))));
            }
        }

        if (!total.GoalReached && total.CycleProgress is { } progress && progress != total.Total)
        {
            parts.Add(Loc.T("stats.note.soFar", ("time", TimeFormat.HoursMinutes(progress))));
        }

        return string.Join(" · ", parts);
    }
}
