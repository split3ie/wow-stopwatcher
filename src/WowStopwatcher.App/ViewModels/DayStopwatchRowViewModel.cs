using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.ViewModels;

/// <summary>A stopwatch's total on one day, shown in the expanded details.</summary>
public sealed partial class DayStopwatchRowViewModel : ObservableObject
{
    private string? _color;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private IBrush? _brush;

    [ObservableProperty]
    private bool _isReached;

    [ObservableProperty]
    private string _timeText = string.Empty;

    [ObservableProperty]
    private GridLength _shareWidth = new(0, GridUnitType.Star);

    [ObservableProperty]
    private GridLength _restWidth = new(1, GridUnitType.Star);

    public void Update(DayStopwatchTotal total)
    {
        var share = Math.Clamp(total.Share, 0, 1);
        Name = total.Name;
        IsReached = total.GoalReached;
        TimeText = TimeFormat.HoursMinutes(total.Total)
            + (total.Goal is { } goal && !total.IsDeleted ? " / " + TimeFormat.GoalLabel(goal) : string.Empty);
        ShareWidth = new GridLength(share, GridUnitType.Star);
        RestWidth = new GridLength(1 - share, GridUnitType.Star);
        if (_color != total.Color)
        {
            _color = total.Color;
            Brush = Avalonia.Media.Brush.Parse(total.Color);
        }
    }
}
