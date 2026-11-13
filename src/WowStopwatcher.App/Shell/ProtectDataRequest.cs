namespace WowStopwatcher.App.Shell;

/// <summary>What the "Protect your data" window needs.</summary>
/// <param name="PinAvailable">True to show the PIN step first.</param>
/// <param name="RecoveryCode">The new recovery code to show.</param>
/// <param name="IsNewDatabase">True when there is no database yet (no backup is made, nothing is converted).</param>
/// <param name="Commit">
/// Runs when the user confirmed, while the window shows its progress state. Gets the chosen PIN (null for "Don't use a PIN" or when there is no
/// PIN step). An exception closes the window and is rethrown to the caller.
/// </param>
public sealed record ProtectDataRequest(bool PinAvailable, string RecoveryCode, bool IsNewDatabase, Func<string?, Task> Commit);
