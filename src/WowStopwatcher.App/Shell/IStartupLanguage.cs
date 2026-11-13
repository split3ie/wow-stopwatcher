namespace WowStopwatcher.App.Shell;

/// <summary>Activates the app language before the first window, so the startup windows are in the user's language.</summary>
public interface IStartupLanguage
{
    /// <summary>Activates the language stored in the settings; null follows the system language.</summary>
    void Apply(string? storedLanguage);
}
