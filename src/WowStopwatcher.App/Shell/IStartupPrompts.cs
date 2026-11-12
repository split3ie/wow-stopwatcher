namespace WowStopwatcher.App.Shell;

/// <summary>The windows of the startup flow. They only collect input; the decisions are made by <see cref="DatabaseUnlock"/>.</summary>
public interface IStartupPrompts
{
    /// <summary>Shows the "Protect your data" window until it closes.</summary>
    /// <exception cref="Exception">Whatever <see cref="ProtectDataRequest.Commit"/> threw.</exception>
    Task<ProtectDataOutcome> ShowProtectDataAsync(ProtectDataRequest request);

    /// <summary>Shows the recovery code window until it closes.</summary>
    Task<RecoveryPromptOutcome> ShowRecoveryPromptAsync(RecoveryPromptRequest request);
}
