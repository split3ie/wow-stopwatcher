namespace WowStopwatcher.App.Shell;

/// <summary>What the recovery code window needs.</summary>
/// <param name="DatabasePath">The database file shown to the user.</param>
/// <param name="TryCode">Checks a typed code; true when it opens the database (the caller keeps the key).</param>
public sealed record RecoveryPromptRequest(string DatabasePath, Func<string, Task<bool>> TryCode);
