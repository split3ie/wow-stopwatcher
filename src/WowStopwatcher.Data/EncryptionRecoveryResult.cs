namespace WowStopwatcher.Data;

/// <summary>Outcome of <see cref="DatabaseEncryption.Recover"/>.</summary>
public enum EncryptionRecoveryResult
{
    /// <summary>No leftovers of an encryption were found.</summary>
    NothingToDo,

    /// <summary>An unfinished encryption was undone; the original plain database is at the path.</summary>
    RolledBack,

    /// <summary>The encrypted database is at the path and the plain leftovers were removed (or are retried at the next start).</summary>
    Completed,

    /// <summary>The state is not one this class produces (or the key is missing for a check it needs). Nothing was deleted; manual attention is needed.</summary>
    Conflict,
}
