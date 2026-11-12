namespace WowStopwatcher.App.Shell;

/// <summary>
/// Asks the user to confirm a sensitive action (export, import, changing the database folder), for example with a PIN.
/// A feature that supports it replaces the default, which allows everything.
/// </summary>
public interface ISensitiveActionGuard
{
    /// <summary>True when the action may go on; false when the user cancelled or failed the check.</summary>
    Task<bool> ConfirmAsync();
}
