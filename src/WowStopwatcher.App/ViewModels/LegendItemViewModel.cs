using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.ViewModels;

/// <summary>An entry in the timeline legend: a stopwatch, "Left to goal" or "Goal reached".</summary>
public sealed partial class LegendItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private IBrush? _brush;

    private string? _color;

    public LegendItemViewModel(LegendKind kind = LegendKind.Stopwatch) => Kind = kind;

    public LegendKind Kind { get; }

    public bool IsStopwatch => Kind == LegendKind.Stopwatch;

    public bool IsRemaining => Kind == LegendKind.Remaining;

    public bool IsGoal => Kind == LegendKind.Goal;

    public void Update(StopwatchDefinition definition)
    {
        Name = definition.Name;
        if (_color != definition.Color)
        {
            _color = definition.Color;
            Brush = Avalonia.Media.Brush.Parse(definition.Color);
        }
    }
}
