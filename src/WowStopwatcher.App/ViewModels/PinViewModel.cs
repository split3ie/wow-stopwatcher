using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.ViewModels;

/// <summary>A goal-reached pin on the timeline.</summary>
public sealed partial class PinViewModel : ObservableObject
{
    /// <summary>Height of the pin above the track at level 0, in pixels.</summary>
    public const double BaseRise = 26;

    /// <summary>Extra height per stacking level, in pixels.</summary>
    public const double RisePerLevel = 24;

    [ObservableProperty]
    private double _position;

    [ObservableProperty]
    private double _rise = BaseRise;

    [ObservableProperty]
    private IBrush? _brush;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subText = string.Empty;

    [ObservableProperty]
    private string _label = string.Empty;

    /// <summary>Set by the view while the pointer is over the pin or it has keyboard focus.</summary>
    [ObservableProperty]
    private bool _isActive;

    private string? _color;

    public void Update(TimelineGoalPin pin, StopwatchDefinition? definition)
    {
        Position = pin.Position;
        Rise = BaseRise + pin.Level * RisePerLevel;
        SubText = pin.SubText;

        var goal = definition?.Goal is { } g ? TimeFormat.GoalLabel(g) : string.Empty;
        var name = definition?.Name ?? string.Empty;
        Title = Loc.T("timeline.pin.title", ("name", name), ("goal", goal));
        Label = Loc.T("timeline.pin.label", ("name", name), ("goal", goal));

        if (_color != pin.Color)
        {
            _color = pin.Color;
            Brush = Avalonia.Media.Brush.Parse(pin.Color);
        }
    }
}
