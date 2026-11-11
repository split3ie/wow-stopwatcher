using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Features.Settings;

/// <summary>
/// The Data section of Settings: where the database lives (change folder, open folder) and the backup file (export, import).
/// A change of folder, a replace and an import do not touch the files here: they store a pending step in <c>location.json</c>
/// and restart the app, which runs the step before the database is opened.
/// </summary>
public sealed partial class DataSettingsViewModel : ObservableObject
{
    public const string BackupExtension = "zip";

    private readonly DatabaseLocation _location;
    private readonly DatabaseTransfer _transfer;
    private readonly SqliteDatabase _database;
    private readonly IFilePickerService _picker;
    private readonly IFolderLauncher _launcher;
    private readonly IDialogService _dialog;
    private readonly IAppLifetime _lifetime;
    private readonly IStopwatchControl _control;
    private readonly TimeProvider _time;

    private string? _existingFolder;

    public DataSettingsViewModel(
        DatabaseLocation location,
        DatabaseTransfer transfer,
        SqliteDatabase database,
        IFilePickerService picker,
        IFolderLauncher launcher,
        IDialogService dialog,
        IAppLifetime lifetime,
        IStopwatchControl control,
        TimeProvider time)
    {
        _location = location;
        _transfer = transfer;
        _database = database;
        _picker = picker;
        _launcher = launcher;
        _dialog = dialog;
        _lifetime = lifetime;
        _control = control;
        _time = time;
    }

    /// <summary>The folder of the database in use.</summary>
    public string FolderPath => Path.GetDirectoryName(_database.Path) ?? _database.Path;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInfoStatus), nameof(HasErrorStatus))]
    private string? _statusText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInfoStatus), nameof(HasErrorStatus))]
    private bool _statusIsError;

    /// <summary>True when the last action left a message that is not an error.</summary>
    public bool HasInfoStatus => !string.IsNullOrEmpty(StatusText) && !StatusIsError;

    public bool HasErrorStatus => !string.IsNullOrEmpty(StatusText) && StatusIsError;

    /// <summary>True while the panel about a database found in the chosen folder is shown.</summary>
    [ObservableProperty]
    private bool _isExistingPanelVisible;

    [ObservableProperty]
    private string _existingTitle = string.Empty;

    [ObservableProperty]
    private string _existingSummary = string.Empty;

    /// <summary>Why the database in the chosen folder cannot be used; null when it can.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseExisting), nameof(HasExistingError))]
    private string? _existingError;

    /// <summary>True when the database found in the chosen folder can be used (not newer, not damaged).</summary>
    public bool CanUseExisting => ExistingError is null;

    public bool HasExistingError => ExistingError is not null;

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        ClearStatus();
        if (!await _launcher.OpenAsync(FolderPath))
        {
            SetStatus(Loc.T("data.folder.openFailed"), isError: true);
        }
    }

    [RelayCommand]
    private async Task ChangeFolderAsync()
    {
        ClearStatus();
        CloseExistingPanel();

        var picked = await _picker.PickFolderAsync(Loc.T("data.folder.pickTitle"));
        if (picked is null)
        {
            return;
        }

        var folder = Path.GetFullPath(picked);
        if (IsSameFolder(folder, FolderPath))
        {
            SetStatus(Loc.T("data.folder.sameFolder"), isError: false);
            return;
        }

        var targetDb = Path.Combine(folder, DatabaseLocation.DatabaseFileName);
        if (!File.Exists(targetDb))
        {
            _dialog.Show(
                Loc.T("data.move.title"),
                Loc.T("data.move.body", ("folder", folder)),
                Loc.T("data.move.confirm"),
                () => StartPending(new PendingStep(PendingKind.Move, _database.Path, targetDb), keepRunning: true));
            return;
        }

        await ShowExistingAsync(folder, targetDb);
    }

    [RelayCommand]
    private void UseExisting()
    {
        if (_existingFolder is not { } folder || !CanUseExisting)
        {
            return;
        }

        try
        {
            _location.SetFolder(folder);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            SetStatus(Loc.T("data.saveFailed", ("reason", ex.Message)), isError: true);
            return;
        }

        // The other database is used from the next start, so the stopwatches are stopped and saved here first.
        _control.StopAll();
        Finish(keepRunning: false);
    }

    [RelayCommand]
    private void ReplaceWithMine()
    {
        if (_existingFolder is not { } folder)
        {
            return;
        }

        StartPending(
            new PendingStep(PendingKind.Replace, _database.Path, Path.Combine(folder, DatabaseLocation.DatabaseFileName)),
            keepRunning: true);
    }

    [RelayCommand]
    private void CancelExisting() => CloseExistingPanel();

    [RelayCommand]
    private async Task ExportAsync()
    {
        ClearStatus();
        var name = $"WowStopwatcher-backup-{_time.GetLocalNow():yyyy-MM-dd}.{BackupExtension}";
        var zipPath = await _picker.PickSaveFileAsync(Loc.T("data.export.pickTitle"), name, BackupExtension);
        if (zipPath is null)
        {
            return;
        }

        try
        {
            await Task.Run(() => _transfer.Export(_database.Path, FolderPath, zipPath));
            SetStatus(Loc.T("data.export.done", ("path", zipPath)), isError: false);
        }
        catch (Exception ex) when (ex is DatabaseTransferException || IsFileError(ex))
        {
            SetStatus(Loc.T("data.export.failed", ("reason", Describe(ex))), isError: true);
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        ClearStatus();
        CloseExistingPanel();
        var zipPath = await _picker.PickOpenFileAsync(Loc.T("data.import.pickTitle"), Loc.T("data.import.filter"), [BackupExtension]);
        if (zipPath is null)
        {
            return;
        }

        TransferErrorCode? error;
        try
        {
            using var validation = await Task.Run(() => _transfer.ValidateImport(zipPath));
            error = validation.Error;
        }
        catch (Exception ex) when (ex is DatabaseTransferException || IsFileError(ex))
        {
            SetStatus(Loc.T("data.import.failed", ("reason", Describe(ex))), isError: true);
            return;
        }

        if (error is { } code)
        {
            SetStatus(Loc.T("data.import.failed", ("reason", TransferMessages.Describe(code))), isError: true);
            return;
        }

        _dialog.Show(
            Loc.T("data.import.confirmTitle"),
            Loc.T("data.import.confirmBody"),
            Loc.T("data.import.confirm"),
            () =>
            {
                // The backup replaces the current data at the next start, so the stopwatches are stopped and saved here first.
                if (StorePending(new PendingStep(PendingKind.Import, zipPath, _database.Path)))
                {
                    _control.StopAll();
                    Finish(keepRunning: false);
                }
            });
    }

    private async Task ShowExistingAsync(string folder, string targetDb)
    {
        string? error = null;
        var summary = string.Empty;
        try
        {
            var info = await Task.Run(() => _transfer.Inspect(targetDb));
            if (info.IsNewerThanApp)
            {
                error = Loc.T("data.existing.newer");
            }
            else if (!info.IntegrityOk)
            {
                error = Loc.T("data.existing.damaged");
            }
            else
            {
                var lastUsed = info.LastSessionStartUtc is { } last
                    ? TimeZoneInfo.ConvertTime(last, _time.LocalTimeZone).ToString("D", Loc.Culture)
                    : Loc.T("data.existing.neverUsed");
                summary = Loc.T(
                    "data.existing.summary",
                    ("lastUsed", lastUsed),
                    ("stopwatches", Loc.Plural("data.existing.stopwatches", info.StopwatchCount)),
                    ("days", Loc.Plural("data.existing.days", info.SessionDayCount)));
            }
        }
        catch (Exception ex) when (ex is DatabaseTransferException or DatabaseOpenException || IsFileError(ex))
        {
            error = Loc.T("data.existing.unavailable");
        }

        _existingFolder = folder;
        ExistingTitle = Loc.T("data.existing.title", ("folder", folder));
        ExistingSummary = summary;
        ExistingError = error;
        IsExistingPanelVisible = true;
    }

    /// <summary>Stores a pending step and restarts, keeping the running stopwatches running.</summary>
    private void StartPending(PendingStep step, bool keepRunning)
    {
        if (StorePending(step))
        {
            Finish(keepRunning);
        }
    }

    private bool StorePending(PendingStep step)
    {
        try
        {
            _location.SetPending(step);
            return true;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            SetStatus(Loc.T("data.saveFailed", ("reason", ex.Message)), isError: true);
            return false;
        }
    }

    /// <summary>
    /// Restarts the app, or quits when it cannot restart itself. A restart keeps open sessions open, so it also covers
    /// the case where they were stopped before (nothing is open then).
    /// </summary>
    private void Finish(bool keepRunning)
    {
        if (_lifetime.CanRestart)
        {
            _lifetime.Restart();
        }
        else if (keepRunning)
        {
            _lifetime.QuitKeepingRunningSessions();
        }
        else
        {
            _lifetime.Quit();
        }
    }

    private void CloseExistingPanel()
    {
        IsExistingPanelVisible = false;
        _existingFolder = null;
        ExistingError = null;
    }

    private void ClearStatus() => SetStatus(null, isError: false);

    private void SetStatus(string? text, bool isError)
    {
        StatusText = text;
        StatusIsError = isError;
    }

    private static string Describe(Exception ex) =>
        TransferMessages.Describe(ex is DatabaseTransferException transfer ? transfer.Code : TransferErrorCode.Io);

    private static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException;

    private static bool IsSameFolder(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
