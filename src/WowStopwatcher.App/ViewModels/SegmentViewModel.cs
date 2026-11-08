using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.ViewModels;

/// <summary>One piece of the timeline track (a logged session or the time left to one goal).</summary>
public sealed partial class SegmentViewModel : ObservableObject
{
    [ObservableProperty]
    private double _start;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private IBrush? _brush;

    [ObservableProperty]
    private bool _hasLanes;

    /// <summary>Stripes of an overlap segment; empty for a single-stopwatch segment.</summary>
    public System.Collections.ObjectModel.ObservableCollection<LaneViewModel> Lanes { get; } = [];

    [ObservableProperty]
    private bool _isRemaining;

    [ObservableProperty]
    private bool _isCapped;

    [ObservableProperty]
    private bool _isLive;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _timeText = string.Empty;

    [ObservableProperty]
    private string? _subText;

    /// <summary>Set by the view while the pointer is over the segment.</summary>
    [ObservableProperty]
    private bool _isHovered;

    /// <summary>Center of the segment as a fraction of the track.</summary>
    public double Center { get; private set; }

    private string? _color;
    private bool _brushIsRemaining;

    public void Update(TimelineSegment segment)
    {
        Start = segment.Start;
        Width = segment.Width;
        Center = segment.Center;
        IsRemaining = segment.Kind == TimelineSegmentKind.Remaining;
        IsCapped = segment.Capped;
        IsLive = segment.IsLive;
        Title = segment.Title;
        SubText = segment.SubText;
        TimeText = segment.Kind == TimelineSegmentKind.Remaining
            ? TimeFormat.HoursMinutes(segment.Duration)
            : segment.IsLive
                ? TimeFormat.Clock(segment.Duration, withMs: true)
                : TimeFormat.Human(segment.Duration);

        HasLanes = LaneViewModel.Sync(Lanes, segment.Lanes);

        if (_color != segment.Color || _brushIsRemaining != IsRemaining)
        {
            _color = segment.Color;
            _brushIsRemaining = IsRemaining;
            if (IsRemaining)
            {
                var tint = Color.Parse(segment.Color);
                Brush = new SolidColorBrush(Color.FromArgb(0x40, tint.R, tint.G, tint.B));
            }
            else
            {
                Brush = Avalonia.Media.Brush.Parse(segment.Color);
            }
        }
    }
}
