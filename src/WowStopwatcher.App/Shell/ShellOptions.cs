namespace WowStopwatcher.App.Shell;

/// <summary>Shell behavior that depends on which features are present.</summary>
public sealed class ShellOptions
{
    /// <summary>
    /// True when closing the main window only hides it (the app keeps running, for example in the tray).
    /// False when closing the window quits the app.
    /// </summary>
    public bool CloseHidesWindow { get; set; }

    /// <summary>
    /// True when a feature can apply a PIN chosen at startup (<see cref="IStartupPinSetup"/>), so the "Protect your data" window shows a PIN step.
    /// A plain flag: startup code reads it before the database is open, so it must not depend on anything built from the database.
    /// </summary>
    public bool StartupPinStep { get; set; }
}
