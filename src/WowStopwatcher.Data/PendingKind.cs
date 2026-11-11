namespace WowStopwatcher.Data;

/// <summary>A step run at the next startup, before the database is opened.</summary>
public enum PendingKind
{
    /// <summary>Copy the database (and templates) to a folder that has no database, then switch to it.</summary>
    Move,

    /// <summary>Back up the database in the target folder, replace it with a copy of the source, then switch to it.</summary>
    Replace,

    /// <summary>Back up the current database, then replace it with the database inside a backup zip.</summary>
    Import,
}
