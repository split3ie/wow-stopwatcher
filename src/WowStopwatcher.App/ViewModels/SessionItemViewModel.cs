using CommunityToolkit.Mvvm.ComponentModel;

namespace WowStopwatcher.App.ViewModels;

/// <summary>One line of the "Sessions today" list of a stopwatch row.</summary>
public sealed partial class SessionItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _number = string.Empty;

    [ObservableProperty]
    private string _duration = string.Empty;

    [ObservableProperty]
    private string _tag = string.Empty;
}
