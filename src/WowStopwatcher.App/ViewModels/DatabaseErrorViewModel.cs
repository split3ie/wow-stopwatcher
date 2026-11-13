using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.Core.Localization;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.ViewModels;

/// <summary>
/// Texts and choices of the window shown when the database cannot be opened.
/// <paramref name="encryptionAvailable"/> is false in a build without encryption, where an encrypted database is explained as unsupported.
/// </summary>
public sealed partial class DatabaseErrorViewModel(DatabaseOpenException error, bool encryptionAvailable = true) : ObservableObject
{
    public string Title { get; } = Loc.T(error.Problem switch
    {
        DatabaseProblem.EncryptionFailed => "dbError.titleEncrypt",
        DatabaseProblem.EncryptionConflict => "dbError.titleConflict",
        DatabaseProblem.KeyRejected or DatabaseProblem.KeyRequired or DatabaseProblem.KeyFileMissing or DatabaseProblem.KeyFileDamaged
            => "dbError.titleKey",
        _ => "dbError.title",
    });

    public string Body { get; } = Loc.T(error.Problem switch
    {
        DatabaseProblem.NewerVersion => "dbError.newerVersion",
        DatabaseProblem.Damaged => "dbError.damaged",
        DatabaseProblem.BackupFailed => "dbError.backupFailed",
        DatabaseProblem.FolderMissing => "dbError.folderMissing",
        DatabaseProblem.LocationDamaged => "dbError.locationDamaged",
        DatabaseProblem.KeyRejected => "dbError.keyRejected",
        DatabaseProblem.KeyRequired => encryptionAvailable ? "dbError.keyRequired" : "dbError.encryptionOff",
        DatabaseProblem.NotEncrypted => "dbError.notEncrypted",
        DatabaseProblem.KeyFileMissing => "dbError.keyFileMissing",
        DatabaseProblem.KeyFileDamaged => "dbError.keyFileDamaged",
        DatabaseProblem.EncryptionConflict => "dbError.encryptionConflict",
        DatabaseProblem.EncryptionFailed => "dbError.encryptionFailed",
        _ => "dbError.unavailable",
    });

    public string FileLabel { get; } = Loc.T(error.Problem switch
    {
        DatabaseProblem.FolderMissing => "dbError.folderLabel",
        DatabaseProblem.LocationDamaged => "dbError.locationFile",
        DatabaseProblem.KeyFileMissing or DatabaseProblem.KeyFileDamaged => "dbError.keyFile",
        _ => "dbError.file",
    });

    public string Path { get; } = error.Problem == DatabaseProblem.FolderMissing
        ? System.IO.Path.GetDirectoryName(error.Path) ?? error.Path
        : error.Path;

    /// <summary>True when a newer app version can open the data, so a download link makes sense.</summary>
    public bool CanDownload { get; } = error.Problem == DatabaseProblem.NewerVersion;

    /// <summary>True when the folder may come back (drive connected, sync finished), so "Try again" makes sense.</summary>
    public bool CanRetry { get; } = error.Problem == DatabaseProblem.FolderMissing;

    /// <summary>True when another folder may hold a usable copy (the folder is missing or the key of this one is lost).</summary>
    public bool CanChooseFolder { get; } = error.Problem is DatabaseProblem.FolderMissing
        or DatabaseProblem.KeyRejected or DatabaseProblem.KeyRequired or DatabaseProblem.KeyFileMissing or DatabaseProblem.KeyFileDamaged;

    public bool CanUseDefault { get; } = error.Problem is DatabaseProblem.FolderMissing or DatabaseProblem.LocationDamaged;

    public string ReleasesUrl => "https://github.com/split3ie/wow-stopwatcher/releases/latest";

    /// <summary>Why the last choice failed; null while there is no failure.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActionError))]
    private string? _actionError;

    public bool HasActionError => !string.IsNullOrEmpty(ActionError);
}
