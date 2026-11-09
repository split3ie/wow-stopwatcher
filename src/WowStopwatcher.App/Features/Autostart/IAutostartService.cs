namespace WowStopwatcher.App.Features.Autostart;

/// <summary>Registers the app to start at login, per user. One implementation per operating system.</summary>
public interface IAutostartService
{
    /// <summary>True when the autostart entry exists.</summary>
    bool IsEnabled();

    /// <summary>Adds or removes the autostart entry. Throws if it cannot be written.</summary>
    void SetEnabled(bool enabled);
}
