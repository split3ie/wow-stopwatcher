using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Navigation;

/// <summary>Resolves page view models from the container; their lifetime (kept alive or new per visit) is set by their registration.</summary>
public sealed class NavigationService : INavigationService
{
    private readonly Dictionary<string, PageDescriptor> _pages;
    private readonly IServiceProvider _provider; // page factory: the one place besides Composition that uses the provider
    private readonly IDialogService _dialog;

    public NavigationService(IEnumerable<PageDescriptor> pages, IServiceProvider provider, IDialogService dialog)
    {
        _pages = pages.ToDictionary(p => p.Key);
        _provider = provider;
        _dialog = dialog;
    }

    public string CurrentKey { get; private set; } = string.Empty;

    public object? CurrentPage { get; private set; }

    public event EventHandler? Navigated;

    public bool CanNavigateTo(string key) => _pages.ContainsKey(key);

    public void NavigateTo(string key)
    {
        if (!_pages.TryGetValue(key, out var descriptor))
        {
            throw new ArgumentException($"Unknown page '{key}'.", nameof(key));
        }

        _dialog.Cancel();
        (CurrentPage as INavigationAware)?.OnNavigatedFrom();

        CurrentKey = key;
        CurrentPage = _provider.GetRequiredService(descriptor.ViewModelType);
        (CurrentPage as INavigationAware)?.OnNavigatedTo();
        Navigated?.Invoke(this, EventArgs.Empty);
    }
}
