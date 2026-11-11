using WowStopwatcher.Core.Localization;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.ViewModels;

/// <summary>Texts of the window shown when the database cannot be opened.</summary>
public sealed class DatabaseErrorViewModel(DatabaseOpenException error)
{
    public string Title { get; } = Loc.T("dbError.title");

    public string Body { get; } = Loc.T(error.Problem switch
    {
        DatabaseProblem.NewerVersion => "dbError.newerVersion",
        DatabaseProblem.Damaged => "dbError.damaged",
        DatabaseProblem.BackupFailed => "dbError.backupFailed",
        _ => "dbError.unavailable",
    });

    public string FileLabel { get; } = Loc.T("dbError.file");

    public string Path { get; } = error.Path;

    /// <summary>True when a newer app version can open the data, so a download link makes sense.</summary>
    public bool CanDownload { get; } = error.Problem == DatabaseProblem.NewerVersion;

    public string ReleasesUrl => "https://github.com/split3ie/wow-stopwatcher/releases/latest";
}
