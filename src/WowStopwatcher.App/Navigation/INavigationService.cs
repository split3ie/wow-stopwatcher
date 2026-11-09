namespace WowStopwatcher.App.Navigation;

/// <summary>Switches between pages.</summary>
public interface INavigationService
{
    /// <summary>Key of the current page; empty before the first navigation.</summary>
    string CurrentKey { get; }

    /// <summary>View model of the current page; null before the first navigation.</summary>
    object? CurrentPage { get; }

    /// <summary>Shows the page with the given key. Closes any open dialog.</summary>
    void NavigateTo(string key);

    /// <summary>True when a page with the given key is registered in this build.</summary>
    bool CanNavigateTo(string key);

    /// <summary>Raised after <see cref="CurrentPage"/> changed.</summary>
    event EventHandler? Navigated;
}
