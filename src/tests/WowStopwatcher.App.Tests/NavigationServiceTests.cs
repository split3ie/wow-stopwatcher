using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Navigation;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Tests;

public class NavigationServiceTests
{
    private readonly List<string> _log = [];

    private NavigationService Create()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new PageA(_log));
        services.AddSingleton(new PageB(_log));
        var pages = new[]
        {
            new PageDescriptor("a", "page.a", null, 0, typeof(PageA)),
            new PageDescriptor("b", "page.b", null, 1, typeof(PageB)),
        };
        return new NavigationService(pages, services.BuildServiceProvider(), new FakeDialog(_log));
    }

    [Fact]
    public void First_navigation_cancels_dialog_notifies_new_page_and_raises_Navigated()
    {
        var nav = Create();
        nav.Navigated += (_, _) => _log.Add("navigated");

        nav.NavigateTo("a");

        Assert.Equal(["dialog-cancel", "a-to", "navigated"], _log);
        Assert.Equal("a", nav.CurrentKey);
        Assert.IsType<PageA>(nav.CurrentPage);
    }

    [Fact]
    public void Navigating_notifies_previous_page_before_new_page()
    {
        var nav = Create();
        nav.NavigateTo("a");
        _log.Clear();
        nav.Navigated += (_, _) => _log.Add("navigated");

        nav.NavigateTo("b");

        Assert.Equal(["dialog-cancel", "a-from", "b-to", "navigated"], _log);
        Assert.Equal("b", nav.CurrentKey);
        Assert.IsType<PageB>(nav.CurrentPage);
    }

    [Fact]
    public void CanNavigateTo_is_true_only_for_registered_pages()
    {
        var nav = Create();

        Assert.True(nav.CanNavigateTo("a"));
        Assert.False(nav.CanNavigateTo("nope"));
    }

    [Fact]
    public void Unknown_key_throws_and_changes_nothing()
    {
        var nav = Create();
        nav.NavigateTo("a");
        _log.Clear();

        Assert.Throws<ArgumentException>(() => nav.NavigateTo("nope"));

        Assert.Empty(_log);
        Assert.Equal("a", nav.CurrentKey);
    }

    private sealed class PageA(List<string> log) : INavigationAware
    {
        public void OnNavigatedTo() => log.Add("a-to");

        public void OnNavigatedFrom() => log.Add("a-from");
    }

    private sealed class PageB(List<string> log) : INavigationAware
    {
        public void OnNavigatedTo() => log.Add("b-to");

        public void OnNavigatedFrom() => log.Add("b-from");
    }

    private sealed class FakeDialog(List<string> log) : IDialogService
    {
        public bool IsOpen => false;

        public string Body { get; set; } = string.Empty;

        public void Show(string title, string body, string confirmLabel, Action onConfirm)
        {
        }

        public void Cancel() => log.Add("dialog-cancel");
    }
}
