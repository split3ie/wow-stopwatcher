using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.App.ViewModels;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.Features.Statistics;

/// <summary>
/// One stopwatch's piece of a day segment on the day bar. Position and size are fractions of the bar:
/// stacked inside its segment normally, full height inside its stopwatch group while combined.
/// </summary>
public sealed partial class DayPieceViewModel : ObservableObject
{
    private readonly DayViewModel _owner;
    private string? _color;

    public DayPieceViewModel(DayViewModel owner) => _owner = owner;

    [ObservableProperty]
    private double _start;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private double _top;

    [ObservableProperty]
    private double _height = 1;

    [ObservableProperty]
    private IBrush? _brush;

    /// <summary>Set by the view while the pointer is over the piece.</summary>
    [ObservableProperty]
    private bool _isHovered;

    /// <summary>Drawn lighter: the pieces of the hovered segment, or of the hovered stopwatch while combined.</summary>
    [ObservableProperty]
    private bool _isHighlighted;

    /// <summary>1 when the right-hand seam shows, 0 when hidden; animated by the view.</summary>
    [ObservableProperty]
    private double _seamOpacity = 1;

    private CombinedPiece _piece = new(0, 0, 1, string.Empty, string.Empty, TimeSpan.Zero, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, true);
    private string _normalTitle = string.Empty;
    private string _combinedTitle = string.Empty;
    private string _normalTimeText = string.Empty;
    private string _combinedTimeText = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string TimeText { get; private set; } = string.Empty;

    /// <summary>Center of what the tooltip describes (the segment, or the stopwatch group while combined) as a fraction of the bar.</summary>
    public double Center { get; private set; }

    /// <summary>Index of the segment this piece belongs to.</summary>
    public int SegmentIndex => _piece.SegmentIndex;

    /// <summary>Index of the stopwatch group this piece belongs to when combined.</summary>
    public int Group => _piece.Group;

    /// <summary>True when combining changes the piece's start, top or height.</summary>
    public bool MovesWhenCombined =>
        _piece.LaneCount > 1 || Math.Abs(_piece.CombinedStart - _piece.NormalStart) > 1e-9;

    partial void OnIsHoveredChanged(bool value) => _owner.OnPieceHoverChanged();

    /// <param name="together">Title of an overlap segment ("A + B · together"), or null for a single stopwatch.</param>
    public void Update(CombinedPiece piece, string name, string? together)
    {
        _piece = piece;
        Width = piece.Width;
        _normalTitle = together ?? $"{name} · session {piece.SegmentIndex + 1}";
        _combinedTitle = $"{name} · combined";
        _normalTimeText = TimeFormat.HoursMinutes(piece.Duration);
        _combinedTimeText = TimeFormat.HoursMinutes(piece.GroupDuration);
        Apply(_owner.IsCombined);
        if (_color != piece.Color)
        {
            _color = piece.Color;
            Brush = Avalonia.Media.Brush.Parse(piece.Color);
        }
    }

    /// <summary>Moves the piece to its chronological or combined place and picks the matching tooltip text.</summary>
    public void Apply(bool combined)
    {
        Start = combined ? _piece.CombinedStart : _piece.NormalStart;
        Top = combined ? 0 : (double)_piece.LaneIndex / _piece.LaneCount;
        Height = combined ? 1 : 1.0 / _piece.LaneCount;
        SeamOpacity = !combined || _piece.IsGroupEnd ? 1 : 0;
        Title = combined ? _combinedTitle : _normalTitle;
        TimeText = combined ? _combinedTimeText : _normalTimeText;
        Center = combined ? _piece.GroupStart + _piece.GroupWidth / 2 : _piece.NormalStart + _piece.Width / 2;
    }
}
