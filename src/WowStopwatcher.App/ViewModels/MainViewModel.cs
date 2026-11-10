using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Navigation;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.ViewModels;

/// <summary>The main window: header (date, navigation) and the current page.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IStopwatchState _state;
    private readonly INavigationService _navigation;
    private readonly IUiDispatcher _dispatcher;
    private readonly IReadOnlyList<IWindowVisibilityAware> _visibilityAware;

    public MainViewModel(
        INavigationService navigation,
        IEnumerable<PageDescriptor> pages,
        ConfirmDialogViewModel dialog,
        IStopwatchState state,
        IUiDispatcher dispatcher,
        IEnumerable<IWindowVisibilityAware> visibilityAware)
    {
        _navigation = navigation;
        _state = state;
        _dispatcher = dispatcher;
        _visibilityAware = [.. visibilityAware];
        Dialog = dialog;

        var ordered = pages.OrderBy(p => p.Order).ToList();
        NavItems = [.. ordered.Select(CreateNavItem)];

        _navigation.Navigated += OnNavigated;
        _state.Changed += OnEngineChanged;
        UpdateDate();

        // The first page in navigation order is shown at startup.
        _navigation.NavigateTo(ordered[0].Key);
    }

    /// <summary>The confirmation dialog shown over the whole window.</summary>
    public ConfirmDialogViewModel Dialog { get; }

    /// <summary>The header navigation buttons, ordered by page order.</summary>
    public IReadOnlyList<NavItemViewModel> NavItems { get; }

    /// <summary>The view model of the current page.</summary>
    public object? CurrentPage => _navigation.CurrentPage;

    [ObservableProperty]
    private string _dateText = string.Empty;

    public void SetWindowVisible(bool visible)
    {
        foreach (var item in _visibilityAware)
        {
            item.SetWindowVisible(visible);
        }
    }

    private NavItemViewModel CreateNavItem(PageDescriptor page)
    {
        var key = page.Key;
        return new NavItemViewModel(page.Key, Loc.T(page.TitleKey), page.IconKey, new RelayCommand(() => _navigation.NavigateTo(key)));
    }

    private void OnNavigated(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CurrentPage));
        foreach (var item in NavItems)
        {
            item.IsActive = item.Key == _navigation.CurrentKey;
        }
    }

    private void OnEngineChanged(object? sender, EventArgs e)
    {
        if (_dispatcher.CheckAccess())
        {
            UpdateDate();
        }
        else
        {
            _dispatcher.Post(UpdateDate);
        }
    }

    private void UpdateDate() =>
        DateText = _state.GetSnapshot().Today.ToString(Loc.T("format.dayLong"), Loc.Culture);
}
