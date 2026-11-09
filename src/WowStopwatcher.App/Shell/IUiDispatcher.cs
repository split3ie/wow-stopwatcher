namespace WowStopwatcher.App.Shell;

/// <summary>Access to the UI thread, so view models and services do not depend on Avalonia's dispatcher directly.</summary>
public interface IUiDispatcher
{
    /// <summary>Runs <paramref name="action"/> on the UI thread later.</summary>
    void Post(Action action);

    /// <summary>True when the caller is on the UI thread.</summary>
    bool CheckAccess();
}
