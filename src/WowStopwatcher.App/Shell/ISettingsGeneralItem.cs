namespace WowStopwatcher.App.Shell;

/// <summary>
/// Marker for the view model of a card in the "General" section of Settings. Register it as transient
/// (a new Settings page gets new instances) together with a <see cref="ViewMapping"/> for its view.
/// </summary>
public interface ISettingsGeneralItem
{
    /// <summary>Position in the section; lower comes first.</summary>
    int Order { get; }
}
