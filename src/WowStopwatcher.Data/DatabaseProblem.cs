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

    /// <summary>The database is encrypted and the key does not open it (wrong or lost key; a damaged encrypted file looks the same).</summary>
    KeyRejected,

    /// <summary>The database is encrypted but it was opened without a key.</summary>
    KeyRequired,

    /// <summary>A key was given but the database file is still a plain SQLite file (it needs to be encrypted first).</summary>
    NotEncrypted,

    /// <summary>An encrypted database has no key file next to it, so its recovery code cannot be checked.</summary>
    KeyFileMissing,

    /// <summary>The key file next to an encrypted database is damaged, unreadable or from a newer app version.</summary>
    KeyFileDamaged,

    /// <summary>Leftovers of an interrupted encryption are in a state the app does not produce, so nothing was touched.</summary>
    EncryptionConflict,

    /// <summary>Encrypting the database failed. The original plain database is unchanged.</summary>
    EncryptionFailed,
}
