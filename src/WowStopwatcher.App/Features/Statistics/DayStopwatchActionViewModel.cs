using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Features.Statistics;

/// <summary>A button a feature adds to a stopwatch's row in an expanded day.</summary>
public sealed partial class DayStopwatchActionViewModel
{
    private readonly IStatisticsStopwatchAction _action;
    private readonly string _stopwatchId;
    private readonly DateOnly _date;

    public DayStopwatchActionViewModel(IStatisticsStopwatchAction action, string stopwatchId, DateOnly date, string automationName)
    {
        _action = action;
        _stopwatchId = stopwatchId;
        _date = date;
        AutomationName = automationName;
    }

    public string Label => _action.Label;

    public string AutomationName { get; }

    [RelayCommand]
    private void Invoke() => _action.Invoke(_stopwatchId, _date);
}
