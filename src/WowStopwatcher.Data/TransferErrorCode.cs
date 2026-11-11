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

    /// <summary>The import file is not a zip archive.</summary>
    NotAZip,

    /// <summary>The zip has no <c>wowstopwatcher.db</c>.</summary>
    NoDatabase,

    /// <summary>The database is damaged (integrity check failed).</summary>
    Damaged,

    /// <summary>The database was written by a newer app version.</summary>
    NewerVersion,

    /// <summary>Another file system error.</summary>
    Io,
}
