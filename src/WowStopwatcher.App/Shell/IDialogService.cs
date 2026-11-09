namespace WowStopwatcher.App.Shell;

/// <summary>The in-window confirmation dialog shared by all pages.</summary>
public interface IDialogService
{
    /// <summary>True while the dialog is shown.</summary>
    bool IsOpen { get; }

    /// <summary>The dialog text; can be updated while the dialog is open.</summary>
    string Body { get; set; }

    /// <summary>Opens the dialog; <paramref name="onConfirm"/> runs only if the user confirms.</summary>
    void Show(string title, string body, string confirmLabel, Action onConfirm);

    /// <summary>Closes the dialog without confirming.</summary>
    void Cancel();
}
