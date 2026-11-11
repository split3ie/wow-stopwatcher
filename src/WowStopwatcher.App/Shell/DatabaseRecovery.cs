using WowStopwatcher.Core.Localization;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Shell;

/// <summary>
/// The choices offered when the database location cannot be used: try again, pick another folder or go back to the default location.
/// Each choice restarts the app; the new start then opens the database. Methods return an error text, or null on success.
/// </summary>
public sealed class DatabaseRecovery(DatabaseLocation location, IFilePickerService picker, IAppLifetime lifetime)
{
    public void TryAgain() => RestartApp();

    public string? UseDefaultLocation()
    {
        try
        {
            location.SetFolder(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }

        RestartApp();
        return null;
    }

    public async Task<string?> ChooseFolderAsync()
    {
        var folder = await picker.PickFolderAsync(Loc.T("dbError.chooseFolderTitle"));
        if (folder is null)
        {
            return null;
        }

        try
        {
            location.SetFolder(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }

        RestartApp();
        return null;
    }

    private void RestartApp()
    {
        if (lifetime.CanRestart)
        {
            lifetime.Restart();
        }
        else
        {
            lifetime.Quit();
        }
    }
}
