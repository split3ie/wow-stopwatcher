namespace WowStopwatcher.App.Shell;

/// <summary>Default <see cref="IPageLock"/>: no page is ever locked.</summary>
public sealed class NullPageLock : IPageLock
{
    public static NullPageLock Instance { get; } = new();

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public bool IsLocked(string pageKey) => false;

    public object? GetLockContent(string pageKey) => null;

    public void RecordActivity(string pageKey)
    {
    }
}
