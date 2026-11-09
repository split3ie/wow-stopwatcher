namespace WowStopwatcher.App.Shell;

/// <summary>Shell behavior that depends on which features are present.</summary>
public sealed class ShellOptions
{
    /// <summary>
    /// True when closing the main window only hides it (the app keeps running, for example in the tray).
    /// False when closing the window quits the app.
    /// </summary>
    public bool CloseHidesWindow { get; init; }
}
