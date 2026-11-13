namespace WowStopwatcher.Data;

/// <summary>What a database path holds, judged from the file header (see <see cref="DatabaseFormatDetector"/>).</summary>
public enum DatabaseFileFormat
{
    /// <summary>No file, or an empty (0 byte) file. SQLite treats an empty file as a new database, so either kind can be created there.</summary>
    Missing,

    /// <summary>The file starts with the SQLite header <c>SQLite format 3\0</c>.</summary>
    Plain,

    /// <summary>A non-empty file without the plain header: an encrypted database (or garbage; a wrong key cannot tell them apart).</summary>
    Encrypted,
}
