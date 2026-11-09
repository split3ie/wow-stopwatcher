using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.ViewModels;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.Features.Statistics;

/// <summary>One day card on the Statistics page. Kept across refreshes so expanded and selected state survive.</summary>
public sealed partial class DayViewModel : ObservableObject
{
    private readonly StatisticsViewModel _owner;
    private DaySegmentViewModel? _hoveredSegment;

    public DayViewModel(StatisticsViewModel owner, DateOnly date)
    {
        _owner = owner;
        Date = date;
    }

    public DateOnly Date { get; }

    /// <summary>The page this card belongs to (for the shared select mode).</summary>
    public StatisticsViewModel Page => _owner;

    public ObservableCollection<DaySegmentViewModel> Segments { get; } = [];

    public ObservableCollection<DayStopwatchRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _sub = string.Empty;

    [ObservableProperty]
    private bool _hasSub;

    [ObservableProperty]
    private bool _allGoalsReached;

    [ObservableProperty]
    private bool _isPartialGoals;

    [ObservableProperty]
    private string _goalsText = string.Empty;

    [ObservableProperty]
    private string _totalText = string.Empty;

    [ObservableProperty]
    private string _sessionCountText = string.Empty;

    [ObservableProperty]
    private string[] _ticks = ["", "", "", ""];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleLabel))]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isTipVisible;

    [ObservableProperty]
    private string _tipTitle = string.Empty;

    [ObservableProperty]
    private string _tipTime = string.Empty;

    [ObservableProperty]
    private double _tipPosition;

    public string ToggleLabel => (IsExpanded ? "Hide" : "Show") + " details for " + Label;

    public string SelectLabel => "Select " + Label;

    partial void OnIsSelectedChanged(bool value) => _owner.OnSelectionChanged();

    internal void OnSegmentHoverChanged()
    {
        _hoveredSegment = Segments.FirstOrDefault(s => s.IsHovered);
        UpdateTip();
    }

    [RelayCommand]
    public void Toggle()
    {
        IsExpanded = !IsExpanded;
        if (!IsExpanded)
        {
            IsTipVisible = false;
        }
    }

    public void Update(DayStatistics day, StatisticsResult result, DateOnly today, IReadOnlyDictionary<string, string> names)
    {
        var format = Date.Year == today.Year ? "dddd, d MMMM" : "dddd, d MMMM yyyy";
        Label = Date.ToString(format, CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(ToggleLabel));
        OnPropertyChanged(nameof(SelectLabel));
        Sub = Date == today ? "Today" : string.Empty;
        HasSub = Sub.Length > 0;

        AllGoalsReached = day.GoalCount > 0 && day.GoalsMet == day.GoalCount;
        IsPartialGoals = day.GoalCount > 0 && day.GoalsMet < day.GoalCount;
        GoalsText = $"{day.GoalsMet} of {day.GoalCount}";
        TotalText = TimeFormat.HoursMinutes(day.Total);
        SessionCountText = day.SessionCount.ToString(CultureInfo.InvariantCulture);

        var labels = result.Ticks.Select(t => t.Label).ToArray();
        if (!Ticks.SequenceEqual(labels))
        {
            Ticks = labels.Length == 4 ? labels : [.. labels, .. Enumerable.Repeat(string.Empty, Math.Max(0, 4 - labels.Length))];
        }

        CollectionSync.Resize(Segments, day.Segments.Count, () => new DaySegmentViewModel(this));
        var start = 0.0;
        for (var i = 0; i < day.Segments.Count; i++)
        {
            var segment = day.Segments[i];
            Segments[i].Update(segment, start, names, i + 1);
            start += segment.WidthFraction;
        }

        CollectionSync.Resize(Rows, day.Totals.Count, () => new DayStopwatchRowViewModel(_owner));
        for (var i = 0; i < day.Totals.Count; i++)
        {
            Rows[i].Update(day.Totals[i]);
        }

        _hoveredSegment = Segments.FirstOrDefault(s => s.IsHovered);
        UpdateTip();
    }

    private void UpdateTip()
    {
        if (_hoveredSegment is null)
        {
            IsTipVisible = false;
            return;
        }

        TipTitle = _hoveredSegment.Title;
        TipTime = _hoveredSegment.TimeText;
        TipPosition = _hoveredSegment.Center;
        IsTipVisible = true;
    }
}
