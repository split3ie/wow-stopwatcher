namespace WowStopwatcher.Data;

/// <summary>The key file next to the database is damaged, unreadable or written by a newer app version.</summary>
public sealed class DatabaseKeyFileException(string path, string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>Full path of the key file.</summary>
    public string Path { get; } = path;
}
