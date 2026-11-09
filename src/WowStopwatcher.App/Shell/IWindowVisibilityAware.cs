namespace WowStopwatcher.App.Shell;

/// <summary>Implemented by view models that change how often they refresh while the main window is hidden or minimized.</summary>
public interface IWindowVisibilityAware
{
    /// <summary>Called by the main window when it becomes visible or hidden.</summary>
    void SetWindowVisible(bool visible);
}
