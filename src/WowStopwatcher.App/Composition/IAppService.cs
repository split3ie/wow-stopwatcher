namespace WowStopwatcher.App.Composition;

/// <summary>
/// A background service started after the main window is created (in registration order)
/// and disposed on exit (in reverse order).
/// </summary>
public interface IAppService : IDisposable
{
    void Start();
}
