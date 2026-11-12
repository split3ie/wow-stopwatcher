namespace WowStopwatcher.Data;

/// <summary>
/// Encrypting a database (or finishing an interrupted encryption) failed. The database at the path is unchanged
/// (still the original plain file) unless the message says otherwise.
/// </summary>
public sealed class DatabaseEncryptionException(string message, Exception? inner = null) : Exception(message, inner);
