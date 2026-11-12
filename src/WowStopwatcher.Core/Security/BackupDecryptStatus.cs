namespace WowStopwatcher.Core.Security;

/// <summary>Outcome of <see cref="BackupEnvelope.TryDecrypt"/>.</summary>
public enum BackupDecryptStatus
{
    /// <summary>The data was decrypted and authenticated.</summary>
    Success,

    /// <summary>The data is not a backup envelope of a supported version, or is too short or structurally damaged.</summary>
    Damaged,

    /// <summary>Authentication failed: the password is wrong, or the header or ciphertext was changed (the two cannot be told apart).</summary>
    WrongPasswordOrTampered,
}
