using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.ViewModels;

/// <summary>The Today timeline card: legend, track segments, "now" marker, goal pins and hover tooltips.</summary>
public sealed partial class TimelineViewModel : ObservableObject
{
    private const int MaxLegendItems = 4;
    private const double HeaderGap = 40;
    private const double PinSegmentGap = 4;
    private const double SegmentTooltipGap = 10;

    public ObservableCollection<LegendItemViewModel> Legend { get; } = [];

    public ObservableCollection<SegmentViewModel> Segments { get; } = [];

    public ObservableCollection<PinViewModel> Pins { get; } = [];

    /// <summary>Space between the card header and the track; grows to make room for stacked pins.</summary>
    [ObservableProperty]
    private Thickness _headerMargin = new(0, 0, 0, HeaderGap);

    [ObservableProperty]
    private double _nowPosition;

    [ObservableProperty]
    private string _loggedText = string.Empty;

    [ObservableProperty]
    private string _remainingText = string.Empty;

    [ObservableProperty]
    private bool _isSegmentTipVisible;

    [ObservableProperty]
    private double _segmentTipPosition;

    [ObservableProperty]
    private string _segmentTipTitle = string.Empty;

    [ObservableProperty]
    private string _segmentTipTime = string.Empty;

    [ObservableProperty]
    private string? _segmentTipSub;

    [ObservableProperty]
    private bool _isPinTipVisible;

    [ObservableProperty]
    private double _pinTipPosition;

    [ObservableProperty]
    private double _pinTipRise;

    [ObservableProperty]
    private string _pinTipTitle = string.Empty;

    [ObservableProperty]
    private string _pinTipSub = string.Empty;

    [ObservableProperty]
    private IBrush? _pinTipBrush;

    public double SegmentTipRise => SegmentTooltipGap;

    public void Update(StopwatchEngineSnapshot snapshot, TodayTimeline timeline)
    {
        // Legend: stopwatch entries only when there are few stopwatches, then the two fixed entries.
        var stopwatches = snapshot.Stopwatches.Count <= MaxLegendItems ? snapshot.Stopwatches : [];
        var legendCount = stopwatches.Count + 2;
        if (Legend.Count != legendCount)
        {
            Legend.Clear();
            foreach (var _ in stopwatches)
            {
                Legend.Add(new LegendItemViewModel());
            }

            Legend.Add(new LegendItemViewModel(LegendKind.Remaining) { Name = "Left to goal" });
            Legend.Add(new LegendItemViewModel(LegendKind.Goal) { Name = "Goal reached" });
        }

        for (var i = 0; i < stopwatches.Count; i++)
        {
            Legend[i].Update(stopwatches[i]);
        }

        CollectionSync.Resize(Segments, timeline.Segments.Count, CreateSegment);
        for (var i = 0; i < timeline.Segments.Count; i++)
        {
            Segments[i].Update(timeline.Segments[i]);
        }

        CollectionSync.Resize(Pins, timeline.Pins.Count, CreatePin);
        for (var i = 0; i < timeline.Pins.Count; i++)
        {
            Pins[i].Update(timeline.Pins[i], snapshot.Find(timeline.Pins[i].StopwatchId));
        }

        NowPosition = timeline.NowPosition;
        HeaderMargin = new Thickness(0, 0, 0, HeaderGap + timeline.MaxPinLevel * PinViewModel.RisePerLevel);
        LoggedText = TimeFormat.Human(timeline.Logged);
        RemainingText = TimeFormat.HoursMinutes(timeline.Remaining) + (timeline.RemainingCapped ? " (shortened)" : string.Empty);

        UpdateTooltips();
    }

    private SegmentViewModel CreateSegment()
    {
        var segment = new SegmentViewModel();
        segment.PropertyChanged += OnHoverChanged;
        return segment;
    }

    private PinViewModel CreatePin()
    {
        var pin = new PinViewModel();
        pin.PropertyChanged += OnHoverChanged;
        return pin;
    }

    private void OnHoverChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SegmentViewModel.IsHovered) or nameof(PinViewModel.IsActive))
        {
            UpdateTooltips();
        }
    }

    // A pin tooltip wins over a segment tooltip, as in the design.
    private void UpdateTooltips()
    {
        var pin = Pins.FirstOrDefault(p => p.IsActive);
        IsPinTipVisible = pin is not null;
        if (pin is not null)
        {
            PinTipPosition = pin.Position;
            PinTipRise = pin.Rise + PinSegmentGap;
            PinTipTitle = pin.Title;
            PinTipSub = pin.SubText;
            PinTipBrush = pin.Brush;
        }

        var segment = pin is null ? Segments.FirstOrDefault(s => s.IsHovered) : null;
        IsSegmentTipVisible = segment is not null;
        if (segment is not null)
        {
            SegmentTipPosition = Math.Clamp(segment.Center, 0.1, 0.9);
            SegmentTipTitle = segment.Title;
            SegmentTipTime = segment.TimeText;
            SegmentTipSub = segment.SubText;
        }
    }
}
