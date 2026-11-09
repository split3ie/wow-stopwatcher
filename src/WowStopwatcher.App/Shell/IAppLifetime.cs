namespace WowStopwatcher.App.Shell;

/// <summary>Controls the life of the application.</summary>
public interface IAppLifetime
{
    /// <summary>Shuts the application down.</summary>
    void Quit();
}
