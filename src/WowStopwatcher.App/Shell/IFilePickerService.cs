namespace WowStopwatcher.App.Shell;

/// <summary>Native folder and file pickers. Every method returns null when the user cancels or the platform cannot show the picker.</summary>
public interface IFilePickerService
{
    /// <summary>Asks for an existing folder and returns its local path.</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>Asks for an existing file. <paramref name="extensions"/> are given without the dot (for example <c>zip</c>).</summary>
    Task<string?> PickOpenFileAsync(string title, string filterName, IReadOnlyList<string> extensions);

    /// <summary>Asks where to save a file (the system dialog confirms an overwrite). <paramref name="extension"/> is given without the dot.</summary>
    Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension);
}
