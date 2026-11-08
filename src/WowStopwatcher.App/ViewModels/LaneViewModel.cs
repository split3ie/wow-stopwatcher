using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.ViewModels;

/// <summary>One stripe of an overlapping bar segment, filled with its stopwatch's color.</summary>
public sealed partial class LaneViewModel : ObservableObject
{
    private string? _color;

    [ObservableProperty]
    private IBrush? _brush;

    public void Update(TimelineLane lane)
    {
        if (_color != lane.Color)
        {
            _color = lane.Color;
            Brush = Avalonia.Media.Brush.Parse(lane.Color);
        }
    }

    /// <summary>Resizes <paramref name="lanes"/> in place to match <paramref name="source"/> (only when more than one lane).</summary>
    internal static bool Sync(System.Collections.ObjectModel.ObservableCollection<LaneViewModel> lanes, IReadOnlyList<TimelineLane> source)
    {
        var count = source.Count > 1 ? source.Count : 0;
        CollectionSync.Resize(lanes, count, () => new LaneViewModel());
        for (var i = 0; i < count; i++)
        {
            lanes[i].Update(source[i]);
        }

        return count > 1;
    }
}
