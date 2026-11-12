namespace WowStopwatcher.App.Shell;

/// <summary>The in-window dialog with text fields (passwords, codes) or just a confirmation, shown over the whole window.</summary>
public interface IPromptDialogService
{
    /// <summary>
    /// Shows the dialog until it is accepted (true) or cancelled (false). A dialog that is already open is cancelled first.
    /// </summary>
    Task<bool> ShowAsync(PromptDialogRequest request);
}
