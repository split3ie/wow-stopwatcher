using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Services;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.ViewModels;

/// <summary>The main window: header (date, navigation) and the current page.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly StopwatchEngine _engine;
    private readonly IStopwatchRepository _stopwatches;
    private readonly ISessionRepository _sessions;
    private readonly TimeProvider _time;
    private readonly GlobalHotkeyService _hotkeys;

    public MainViewModel(
        StopwatchEngine engine,
        IStopwatchRepository stopwatches,
        ISessionRepository sessions,
        TimeProvider time,
        GlobalHotkeyService hotkeys)
    {
        _engine = engine;
        _stopwatches = stopwatches;
        _sessions = sessions;
        _time = time;
        _hotkeys = hotkeys;

        Dialog = new ConfirmDialogViewModel();
        Today = new TodayViewModel(engine, stopwatches, Dialog, () => NavigateTo(AppPage.Settings));
        _currentPage = Today;

        _engine.Changed += OnEngineChanged;
        UpdateDate();
    }

    /// <summary>The confirmation dialog shown over the whole window.</summary>
    public ConfirmDialogViewModel Dialog { get; }

    public TodayViewModel Today { get; }

    public AppPage Page { get; private set; } = AppPage.Today;

    /// <summary>The view model of the current page: Today, Statistics or Settings.</summary>
    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private string _dateText = string.Empty;

    public bool IsTodayActive => Page == AppPage.Today;

    public bool IsStatisticsActive => Page == AppPage.Statistics;

    public bool IsSettingsActive => Page == AppPage.Settings;

    public void SetWindowVisible(bool visible) => Today.SetWindowVisible(visible);

    /// <summary>Switches page. Leaving Settings reloads the stopwatch definitions so changes show up.</summary>
    public void NavigateTo(AppPage page)
    {
        Dialog.Cancel();
        var previous = Page;
        Page = page;

        if (previous == AppPage.Settings && page != AppPage.Settings)
        {
            (CurrentPage as SettingsViewModel)?.StopRecording();
            _engine.ReloadDefinitions();
        }

        Today.SetPageActive(page == AppPage.Today);
        CurrentPage = page switch
        {
            AppPage.Today => Today,
            AppPage.Statistics => new StatisticsViewModel(_engine, _stopwatches, _sessions, _time, Dialog),
            AppPage.Settings => new SettingsViewModel(_engine, _stopwatches, _sessions, _time, Dialog, _hotkeys),
            _ => throw new ArgumentOutOfRangeException(nameof(page)),
        };

        OnPropertyChanged(nameof(IsTodayActive));
        OnPropertyChanged(nameof(IsStatisticsActive));
        OnPropertyChanged(nameof(IsSettingsActive));
    }

    [RelayCommand]
    private void ShowToday() => NavigateTo(AppPage.Today);

    [RelayCommand]
    private void ShowStatistics() => NavigateTo(AppPage.Statistics);

    [RelayCommand]
    private void ShowSettings() => NavigateTo(AppPage.Settings);

    private void OnEngineChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateDate();
        }
        else
        {
            Dispatcher.UIThread.Post(UpdateDate);
        }
    }

    private void UpdateDate() =>
        DateText = _engine.GetSnapshot().Today.ToString("dddd, d MMMM", CultureInfo.InvariantCulture);
}
