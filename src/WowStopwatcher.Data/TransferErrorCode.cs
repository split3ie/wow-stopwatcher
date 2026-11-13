namespace WowStopwatcher.Data;

/// <summary>Why a copy, export, import or pending step failed. The UI maps these to localized messages.</summary>
public enum TransferErrorCode
{
    /// <summary>The source database or file does not exist.</summary>
    SourceMissing,

    /// <summary>The target database already exists and is never overwritten.</summary>
    TargetExists,

    /// <summary>The copy failed or did not pass the integrity check.</summary>
    CopyFailed,

    /// <summary>A backup that must be made first could not be made.</summary>
    BackupFailed,

    /// <summary>The import file is neither a zip archive nor a <c>.wsbackup</c> file.</summary>
    NotAZip,

    /// <summary>The zip has no <c>wowstopwatcher.db</c>.</summary>
    NoDatabase,

    /// <summary>The database is damaged (integrity check failed).</summary>
    Damaged,

    /// <summary>The database was written by a newer app version.</summary>
    NewerVersion,

    /// <summary>The database is encrypted and no key was given. The UI asks for that database's recovery code.</summary>
    KeyRequired,

    /// <summary>The database is encrypted and the given key does not open it.</summary>
    KeyRejected,

    /// <summary>An encrypted database has no key file next to it, where one is needed (export).</summary>
    KeyFileMissing,

    /// <summary>The backup file (<c>.wsbackup</c>) is password-protected and no password was given.</summary>
    PasswordRequired,

    /// <summary>The backup file's password is wrong (or the file was changed: the two cannot be told apart).</summary>
    WrongPassword,

    /// <summary>Another file system error.</summary>
    Io,
}
