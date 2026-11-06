using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.ViewModels;

/// <summary>One session on a day bar. Start and width are fractions of the shared bar scale.</summary>
public sealed partial class DaySegmentViewModel : ObservableObject
{
    private readonly DayViewModel _owner;
    private string? _color;

    public DaySegmentViewModel(DayViewModel owner) => _owner = owner;

    [ObservableProperty]
    private double _start;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private IBrush? _brush;

    /// <summary>Set by the view while the pointer is over the segment.</summary>
    [ObservableProperty]
    private bool _isHovered;

    public string Title { get; private set; } = string.Empty;

    public string TimeText { get; private set; } = string.Empty;

    /// <summary>Center of the segment as a fraction of the bar.</summary>
    public double Center => Start + Width / 2;

    partial void OnIsHoveredChanged(bool value) => _owner.OnSegmentHoverChanged();

    public void Update(DaySegment segment, double start, string stopwatchName, int number)
    {
        Start = start;
        Width = segment.WidthFraction;
        Title = $"{stopwatchName} · session {number}";
        TimeText = TimeFormat.HoursMinutes(segment.Duration);
        if (_color != segment.Color)
        {
            _color = segment.Color;
            Brush = Avalonia.Media.Brush.Parse(segment.Color);
        }
    }
}
