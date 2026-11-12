namespace WowStopwatcher.App.Shell;

/// <summary>How the "Protect your data" window ended.</summary>
public enum ProtectDataOutcome
{
    /// <summary>The user confirmed and <see cref="ProtectDataRequest.Commit"/> finished.</summary>
    Done,

    /// <summary>The user closed the window before confirming; nothing was changed.</summary>
    Cancelled,
}
