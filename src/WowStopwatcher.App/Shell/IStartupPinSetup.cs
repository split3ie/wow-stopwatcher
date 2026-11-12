namespace WowStopwatcher.App.Shell;

/// <summary>
/// Lets the "Protect your data" window offer a PIN (for the Invoices page). A feature that supports a PIN replaces the default
/// <see cref="NullStartupPinSetup"/> and sets <see cref="ShellOptions.StartupPinStep"/>; without one the window has no PIN step.
/// </summary>
public interface IStartupPinSetup
{
    /// <summary>Called once at startup after the database is open and settings are usable. <paramref name="pin"/> is null for "Don't use a PIN".</summary>
    void Apply(string? pin);
}
