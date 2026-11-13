namespace WowStopwatcher.App.Shell;

/// <summary>What the in-window input dialog shows: text, up to three fields, a note and the check that decides whether it can close.</summary>
public sealed class PromptDialogRequest
{
    public PromptDialogRequest(string title, string confirmLabel)
    {
        Title = title;
        ConfirmLabel = confirmLabel;
    }

    public string Title { get; }

    public string ConfirmLabel { get; }

    /// <summary>Explanation under the title.</summary>
    public string? Body { get; init; }

    /// <summary>A line in the monospace font under the title (a file name or a folder).</summary>
    public string? Subtitle { get; init; }

    public PromptDialogField? First { get; init; }

    public PromptDialogField? Second { get; init; }

    public PromptDialogField? Third { get; init; }

    /// <summary>A highlighted warning above the buttons.</summary>
    public string? Note { get; init; }

    /// <summary>True to draw the confirm button as a destructive action.</summary>
    public bool IsDestructive { get; init; }

    /// <summary>
    /// Runs when the user confirms, with the text of the two fields (empty for a field that is not shown). Returns the error to show and keep
    /// the dialog open, or null to accept and close. May run slow work; the dialog is disabled meanwhile. Without it the dialog just confirms.
    /// </summary>
    public Func<string, string, Task<string?>>? Validate { get; init; }

    /// <summary>
    /// Like <see cref="Validate"/> but receives the text of all three fields (empty for a field that is not shown). Used instead of
    /// <see cref="Validate"/> when set.
    /// </summary>
    public Func<string, string, string, Task<string?>>? ValidateAll { get; init; }
}
