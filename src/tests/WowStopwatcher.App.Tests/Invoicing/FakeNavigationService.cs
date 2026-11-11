using WowStopwatcher.App.Navigation;

namespace WowStopwatcher.App.Tests.Invoicing;

internal sealed class FakeNavigationService : INavigationService
{
    public List<string> Requests { get; } = [];

    public string CurrentKey => Requests.LastOrDefault() ?? string.Empty;

    public object? CurrentPage => null;

    public event EventHandler? Navigated
    {
        add { }
        remove { }
    }

    public bool CanNavigateTo(string key) => true;

    public void NavigateTo(string key) => Requests.Add(key);
}
