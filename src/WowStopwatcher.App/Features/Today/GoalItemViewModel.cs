using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.Features.Today;

/// <summary>An in-progress goal in the Goals card.</summary>
public sealed partial class GoalItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _percentText = string.Empty;

    [ObservableProperty]
    private string _subText = string.Empty;

    [ObservableProperty]
    private IBrush? _brush;

    [ObservableProperty]
    private GridLength _doneWidth = new(0, GridUnitType.Star);

    [ObservableProperty]
    private GridLength _leftWidth = new(1, GridUnitType.Star);

    private string? _color;

    public void Update(StopwatchDefinition definition, TimeSpan total)
    {
        var goal = definition.Goal!.Value;
        var fraction = Math.Clamp(total / goal, 0, 1);

        Name = definition.Name;
        PercentText = $"{(int)Math.Floor(fraction * 100)}%";
        SubText = $"{TimeFormat.HoursMinutes(goal - total)} left of {TimeFormat.GoalLabel(goal)}";
        DoneWidth = new GridLength(fraction, GridUnitType.Star);
        LeftWidth = new GridLength(1 - fraction, GridUnitType.Star);

        if (_color != definition.Color)
        {
            _color = definition.Color;
            Brush = Avalonia.Media.Brush.Parse(definition.Color);
        }
    }
}
