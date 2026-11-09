namespace WowStopwatcher.App.Shell;

/// <summary>
/// Marker for the view model of an item in the right-hand side of the Settings footer. Register it as transient
/// together with a <see cref="ViewMapping"/> for its view.
/// </summary>
public interface ISettingsFooterItem
{
    /// <summary>Position in the footer; lower comes first.</summary>
    int Order { get; }
}
