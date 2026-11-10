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
    private DayPieceViewModel? _hoveredPiece;

    public DayViewModel(StatisticsViewModel owner, DateOnly date)
    {
        _owner = owner;
        Date = date;
    }

    public DateOnly Date { get; }

    /// <summary>The page this card belongs to (for the shared select mode).</summary>
    public StatisticsViewModel Page => _owner;

    public ObservableCollection<DayPieceViewModel> Pieces { get; } = [];

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

    /// <summary>Groups the bar's pieces by stopwatch (and overlap combination) instead of time order. Not saved.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CombineLabel))]
    private bool _isCombined;

    /// <summary>False when combining would not move any piece, so the button is hidden.</summary>
    [ObservableProperty]
    private bool _canCombine;

    private bool _tipSuppressed;

    public string CombineLabel => (IsCombined ? "Show sessions in time order for " : "Combine sessions for ") + Label;

    public string ToggleLabel => (IsExpanded ? "Hide" : "Show") + " details for " + Label;

    public string SelectLabel => "Select " + Label;

    partial void OnIsSelectedChanged(bool value) => _owner.OnSelectionChanged();

    partial void OnIsCombinedChanged(bool value)
    {
        foreach (var piece in Pieces)
        {
            piece.Apply(value);
        }

        // The pieces slide away from under the pointer: hide the tip until the hover changes again.
        _tipSuppressed = true;
        RefreshHover();
    }

    internal void OnPieceHoverChanged()
    {
        _tipSuppressed = false;
        RefreshHover();
    }

    private void RefreshHover()
    {
        _hoveredPiece = Pieces.FirstOrDefault(p => p.IsHovered);
        foreach (var piece in Pieces)
        {
            piece.IsHighlighted = _hoveredPiece is not null
                && (IsCombined ? piece.Group == _hoveredPiece.Group : piece.SegmentIndex == _hoveredPiece.SegmentIndex);
        }

        UpdateTip();
    }

    [RelayCommand]
    private void ToggleCombine() => IsCombined = !IsCombined;

    [RelayCommand]
    public void Toggle()
    {
        IsExpanded = !IsExpanded;
        if (!IsExpanded)
        {
            IsTipVisible = false;
        }
    }

    public void Update(DayStatistics day, StatisticsResult result, DateOnly today, IReadOnlyDictionary<string, string> names, TimeZoneInfo zone)
    {
        var format = Date.Year == today.Year ? "dddd, d MMMM" : "dddd, d MMMM yyyy";
        Label = Date.ToString(format, CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(ToggleLabel));
        OnPropertyChanged(nameof(SelectLabel));
        OnPropertyChanged(nameof(CombineLabel));
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

        var pieces = CombinedTimeline.Build(day.Segments);
        CollectionSync.Resize(Pieces, pieces.Count, () => new DayPieceViewModel(this));
        string NameOf(string id) => names.GetValueOrDefault(id, string.Empty);
        for (var i = 0; i < pieces.Count; i++)
        {
            var lanes = day.Segments[pieces[i].SegmentIndex].Lanes;
            var together = lanes.Count > 1 ? $"{string.Join(" + ", lanes.Select(l => NameOf(l.StopwatchId)))} · together" : null;
            Pieces[i].Update(pieces[i], NameOf(pieces[i].StopwatchId), together);
        }

        CanCombine = Pieces.Any(p => p.MovesWhenCombined);

        CollectionSync.Resize(Rows, day.Totals.Count, () => new DayStopwatchRowViewModel(_owner));
        for (var i = 0; i < day.Totals.Count; i++)
        {
            Rows[i].Update(day.Totals[i], Date, zone);
        }

        RefreshHover();
    }

    private void UpdateTip()
    {
        if (_hoveredPiece is null || _tipSuppressed)
        {
            IsTipVisible = false;
            return;
        }

        TipTitle = _hoveredPiece.Title;
        TipTime = _hoveredPiece.TimeText;
        TipPosition = _hoveredPiece.Center;
        IsTipVisible = true;
    }
}
