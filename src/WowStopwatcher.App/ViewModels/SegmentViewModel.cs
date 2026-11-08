using Avalonia;
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

    /// <summary>Solid stopwatch color for the rounded right edge of a goal piece; null for logged pieces.</summary>
    [ObservableProperty]
    private IBrush? _edgeBrush;

    [ObservableProperty]
    private bool _hasLanes;

    /// <summary>Stripes of an overlap segment; empty for a single-stopwatch segment.</summary>
    public System.Collections.ObjectModel.ObservableCollection<LaneViewModel> Lanes { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CornerRadius))]
    private bool _isRemaining;

    [ObservableProperty]
    private bool _isCapped;

    /// <summary>Goal pieces have a rounded right end.</summary>
    public CornerRadius CornerRadius => IsRemaining ? new CornerRadius(0, 7, 7, 0) : default;

    /// <summary>
    /// True for a goal piece that follows another goal piece: its fill reaches under the previous piece's
    /// rounded end, so the corners show this piece's color instead of the track background.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FillMargin))]
    private bool _extendsLeft;

    public Thickness FillMargin => ExtendsLeft ? new Thickness(-7, 0, 0, 0) : default;

    /// <summary>Earlier goal pieces draw above later ones, so their rounded ends sit on top.</summary>
    [ObservableProperty]
    private int _zIndex;

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
                Brush = CreateRemainingBrush(Color.Parse(segment.Color));
                EdgeBrush = Avalonia.Media.Brush.Parse(segment.Color);
            }
            else
            {
                Brush = Avalonia.Media.Brush.Parse(segment.Color);
                EdgeBrush = null;
            }
        }
    }

    /// <summary>Diagonal stripes in the stopwatch color, same pattern as StripeBrush in Tokens.axaml.</summary>
    private static IBrush CreateRemainingBrush(Color color)
    {
        var background = new GeometryDrawing
        {
            Brush = new SolidColorBrush(Color.FromArgb(0x1A, color.R, color.G, color.B)),
            Geometry = Geometry.Parse("M0,0 H17 V17 H0 Z"),
        };
        var stripes = new GeometryDrawing
        {
            Brush = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B)),
            Geometry = Geometry.Parse("M0,0 L8.5,0 L0,8.5 Z M17,0 L17,8.5 L8.5,17 L0,17 Z"),
        };
        var group = new DrawingGroup();
        group.Children.Add(background);
        group.Children.Add(stripes);
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, 17, 17, RelativeUnit.Absolute),
        };
    }
}
