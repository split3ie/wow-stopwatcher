using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.Core.Localization;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.ViewModels;

/// <summary>Texts and choices of the window shown when the database cannot be opened.</summary>
public sealed partial class DatabaseErrorViewModel(DatabaseOpenException error) : ObservableObject
{
    public string Title { get; } = Loc.T("dbError.title");

    public string Body { get; } = Loc.T(error.Problem switch
    {
        DatabaseProblem.NewerVersion => "dbError.newerVersion",
        DatabaseProblem.Damaged => "dbError.damaged",
        DatabaseProblem.BackupFailed => "dbError.backupFailed",
        DatabaseProblem.FolderMissing => "dbError.folderMissing",
        DatabaseProblem.LocationDamaged => "dbError.locationDamaged",
        _ => "dbError.unavailable",
    });

    public string FileLabel { get; } = Loc.T(error.Problem switch
    {
        DatabaseProblem.FolderMissing => "dbError.folderLabel",
        DatabaseProblem.LocationDamaged => "dbError.locationFile",
        _ => "dbError.file",
    });

    public string Path { get; } = error.Problem == DatabaseProblem.FolderMissing
        ? System.IO.Path.GetDirectoryName(error.Path) ?? error.Path
        : error.Path;

    /// <summary>True when a newer app version can open the data, so a download link makes sense.</summary>
    public bool CanDownload { get; } = error.Problem == DatabaseProblem.NewerVersion;

    /// <summary>True when the folder may come back (drive connected, sync finished), so "Try again" makes sense.</summary>
    public bool CanRetry { get; } = error.Problem == DatabaseProblem.FolderMissing;

    public bool CanChooseFolder { get; } = error.Problem == DatabaseProblem.FolderMissing;

    public bool CanUseDefault { get; } = error.Problem is DatabaseProblem.FolderMissing or DatabaseProblem.LocationDamaged;

    public string ReleasesUrl => "https://github.com/split3ie/wow-stopwatcher/releases/latest";

    /// <summary>Why the last choice failed; null while there is no failure.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActionError))]
    private string? _actionError;

    public bool HasActionError => !string.IsNullOrEmpty(ActionError);
}
