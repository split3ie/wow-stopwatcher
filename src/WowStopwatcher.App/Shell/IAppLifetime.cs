namespace WowStopwatcher.App.Shell;

/// <summary>Controls the life of the application.</summary>
public interface IAppLifetime
{
    /// <summary>Shuts the application down.</summary>
    void Quit();

    /// <summary>True after <see cref="QuitKeepingRunningSessions"/>: running stopwatches are not stopped on exit.</summary>
    bool KeepsRunningSessions { get; }

    /// <summary>Shuts the application down without stopping running stopwatches; their open sessions are resumed by the next start.</summary>
    void QuitKeepingRunningSessions();

    /// <summary>True when the app can start itself again (the path of the running process is known).</summary>
    bool CanRestart { get; }

    /// <summary>Starts a new instance of the app and quits this one; running stopwatches keep running. Does nothing when <see cref="CanRestart"/> is false.</summary>
    void Restart();
}
