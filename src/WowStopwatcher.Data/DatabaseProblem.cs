namespace WowStopwatcher.Data;

/// <summary>Why a database file cannot be used safely.</summary>
public enum DatabaseProblem
{
    /// <summary>The file was written by a newer app version.</summary>
    NewerVersion,

    /// <summary>The file is damaged or is not a SQLite database.</summary>
    Damaged,

    /// <summary>The file could not be opened (in use, no access, other failure).</summary>
    Unavailable,

    /// <summary>A backup required before updating the schema could not be made.</summary>
    BackupFailed,

    /// <summary><c>location.json</c> exists but cannot be read, so the database location is unknown.</summary>
    LocationDamaged,

    /// <summary>The configured database folder does not exist (unplugged drive, sync not finished). No database is created.</summary>
    FolderMissing,
}
