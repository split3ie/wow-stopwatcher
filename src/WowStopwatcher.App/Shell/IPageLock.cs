namespace WowStopwatcher.App.Shell;

/// <summary>
/// Locks a page behind something the user must do first, for example a PIN. A feature that supports it replaces
/// the default, which never locks.
/// </summary>
public interface IPageLock
{
    /// <summary>True while the page with <paramref name="pageKey"/> is locked.</summary>
    bool IsLocked(string pageKey);

    /// <summary>The view model shown instead of the page while it is locked (resolved through the view locator), or null when the page has none.</summary>
    object? GetLockContent(string pageKey);

    /// <summary>Called for user input on the page; restarts the idle timer.</summary>
    void RecordActivity(string pageKey);

    /// <summary>Raised when <see cref="IsLocked"/> may have changed for any page.</summary>
    event EventHandler? Changed;
}
