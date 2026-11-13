namespace WowStopwatcher.App.Shell;

/// <summary>How the recovery code window ended.</summary>
public enum RecoveryPromptOutcome
{
    /// <summary>A correct code was entered.</summary>
    Unlocked,

    /// <summary>The user quit or closed the window.</summary>
    Quit,
}
