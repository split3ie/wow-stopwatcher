using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;
using WowStopwatcher.Core.Security;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Features.Settings;

/// <summary>
/// The Data section of Settings: where the database lives (change folder, open folder) and the backup file (export, import).
/// A change of folder, a replace and an import do not touch the files here: they store a pending step in <c>location.json</c>
/// and restart the app, which runs the step before the database is opened.
/// Export, import and a folder change first ask <see cref="ISensitiveActionGuard"/>. A backup is protected with a password
/// (<see cref="DatabaseTransfer.ExportEncrypted"/>); an imported protected backup is staged next to the database
/// (<see cref="DatabaseTransfer.StageImport"/>) and its key goes to the key store.
/// Without <see cref="SecurityCapabilities.Encryption"/> a backup is the plain <c>.zip</c> (<see cref="DatabaseTransfer.Export"/>) with no password,
/// only <c>.zip</c> files are imported, and a <c>.wsbackup</c> or an encrypted database in another folder is refused.
/// </summary>
public sealed partial class DataSettingsViewModel : ObservableObject
{
    /// <summary>Extension (without the dot) of a password-protected backup.</summary>
    public const string BackupExtension = "wsbackup";

    /// <summary>Extension (without the dot) of the old, unprotected backup; it can still be imported.</summary>
    public const string LegacyBackupExtension = "zip";

    private readonly DatabaseLocation _location;
    private readonly DatabaseTransfer _transfer;
    private readonly SqliteDatabase _database;
    private readonly IFilePickerService _picker;
    private readonly IFolderLauncher _launcher;
    private readonly IDialogService _dialog;
    private readonly IAppLifetime _lifetime;
    private readonly IStopwatchControl _control;
    private readonly TimeProvider _time;
    private readonly IDatabaseKeyProvider _keys;
    private readonly IDatabaseKeyStore _keyStore;
    private readonly ISensitiveActionGuard _guard;
    private readonly IPromptDialogService _prompt;
    private readonly SecurityCapabilities _security;

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
        TimeProvider time,
        IDatabaseKeyProvider keys,
        IDatabaseKeyStore keyStore,
        ISensitiveActionGuard guard,
        IPromptDialogService prompt,
        SecurityCapabilities security)
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
        _keys = keys;
        _keyStore = keyStore;
        _guard = guard;
        _prompt = prompt;
        _security = security;
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
        if (!await _guard.ConfirmAsync())
        {
            return;
        }

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
        if (!await _guard.ConfirmAsync())
        {
            return;
        }

        ClearStatus();
        if (!_security.Encryption)
        {
            await ExportPlainAsync();
            return;
        }

        if (_keys.Key is not { } key)
        {
            SetStatus(Loc.T("data.export.failed", ("reason", TransferMessages.Describe(TransferErrorCode.KeyRequired))), isError: true);
            return;
        }

        var password = await AskExportPasswordAsync();
        if (password is null)
        {
            return;
        }

        var name = $"WowStopwatcher-backup-{_time.GetLocalNow():yyyy-MM-dd}.{BackupExtension}";
        var path = await _picker.PickSaveFileAsync(Loc.T("data.export.pickTitle"), name, BackupExtension);
        if (path is null)
        {
            return;
        }

        try
        {
            await Task.Run(() => _transfer.ExportEncrypted(_database.Path, FolderPath, path, key, password));
            SetStatus(Loc.T("data.export.done", ("path", path)), isError: false);
        }
        catch (Exception ex) when (ex is DatabaseTransferException || IsFileError(ex))
        {
            SetStatus(Loc.T("data.export.failed", ("reason", Describe(ex))), isError: true);
        }
    }

    /// <summary>Writes the old plain <c>.zip</c> backup (a build without encryption).</summary>
    private async Task ExportPlainAsync()
    {
        var name = $"WowStopwatcher-backup-{_time.GetLocalNow():yyyy-MM-dd}.{LegacyBackupExtension}";
        var path = await _picker.PickSaveFileAsync(Loc.T("data.export.pickTitle"), name, LegacyBackupExtension);
        if (path is null)
        {
            return;
        }

        try
        {
            await Task.Run(() => _transfer.Export(_database.Path, FolderPath, path));
            SetStatus(Loc.T("data.export.done", ("path", path)), isError: false);
        }
        catch (Exception ex) when (ex is DatabaseTransferException || IsFileError(ex))
        {
            SetStatus(Loc.T("data.export.failed", ("reason", Describe(ex))), isError: true);
        }
    }

    /// <summary>Asks for the backup password twice; null when the user cancelled.</summary>
    private async Task<string?> AskExportPasswordAsync()
    {
        string? password = null;
        var request = new PromptDialogRequest(Loc.T("data.exportPassword.title"), Loc.T("data.exportPassword.action"))
        {
            Body = Loc.T("data.exportPassword.body"),
            First = new PromptDialogField(Loc.T("data.password.label"), Masked: true),
            Second = new PromptDialogField(Loc.T("data.exportPassword.confirm"), Masked: true),
            Note = Loc.T("data.exportPassword.note"),
            Validate = (first, second) =>
            {
                if (first.Length == 0)
                {
                    return Task.FromResult<string?>(Loc.T("data.exportPassword.empty"));
                }

                if (!BackupEnvelope.IsAcceptablePassword(first))
                {
                    return Task.FromResult<string?>(Loc.T("data.exportPassword.tooShort", ("min", BackupEnvelope.MinPasswordLength)));
                }

                if (first != second)
                {
                    return Task.FromResult<string?>(Loc.T("data.exportPassword.mismatch"));
                }

                password = first;
                return Task.FromResult<string?>(null);
            },
        };

        return await _prompt.ShowAsync(request) ? password : null;
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (!await _guard.ConfirmAsync())
        {
            return;
        }

        ClearStatus();
        CloseExistingPanel();
        var path = await _picker.PickOpenFileAsync(
            Loc.T("data.import.pickTitle"), Loc.T("data.import.filter"),
            _security.Encryption ? [BackupExtension, LegacyBackupExtension] : [LegacyBackupExtension]);
        if (path is null)
        {
            return;
        }

        ImportValidation? validation = null;
        string? staged = null;
        try
        {
            validation = await Task.Run(() => _transfer.ValidateImport(path));
            if (validation.Error == TransferErrorCode.PasswordRequired && !_security.Encryption)
            {
                // A password-protected .wsbackup: this build cannot read it. Nothing is changed.
                SetStatus(Loc.T("data.import.failed", ("reason", Loc.T("transfer.error.encryptionOff"))), isError: true);
                return;
            }

            if (validation.Error == TransferErrorCode.PasswordRequired)
            {
                validation.Dispose();
                validation = await AskBackupPasswordAsync(path);
                if (validation is null)
                {
                    return;
                }
            }

            if (validation.Error is { } code)
            {
                SetStatus(Loc.T("data.import.failed", ("reason", TransferMessages.Describe(code))), isError: true);
                return;
            }

            // A protected backup is copied next to the database now, so that the restart does not need the password.
            var source = path;
            var key = validation.Key;
            var databaseId = validation.KeyFile?.DatabaseId;
            if (key is not null && databaseId is not null)
            {
                var current = validation;
                source = staged = await Task.Run(() => _transfer.StageImport(current, FolderPath));
            }

            if (!await _prompt.ShowAsync(new PromptDialogRequest(Loc.T("data.import.confirmTitle"), Loc.T("data.import.confirm"))
                {
                    Body = Loc.T("data.import.confirmBody"),
                    IsDestructive = true,
                }))
            {
                return;
            }

            if (key is not null && databaseId is { } id)
            {
                TrySaveKey(id, key);
            }

            // The backup replaces the current data at the next start, so the stopwatches are stopped and saved here first.
            if (StorePending(new PendingStep(PendingKind.Import, source, _database.Path)))
            {
                staged = null; // the pending step owns it now
                _control.StopAll();
                Finish(keepRunning: false);
            }
        }
        catch (Exception ex) when (ex is DatabaseTransferException || IsFileError(ex))
        {
            SetStatus(Loc.T("data.import.failed", ("reason", Describe(ex))), isError: true);
        }
        finally
        {
            validation?.Dispose();
            if (staged is not null)
            {
                _transfer.DiscardStaging(staged);
            }
        }
    }

    /// <summary>
    /// Asks for the password of a protected backup until it is right. Returns the checked backup (the caller disposes it), or null when
    /// the user cancelled. A backup that is damaged although the password was right is returned with its error.
    /// </summary>
    private async Task<ImportValidation?> AskBackupPasswordAsync(string path)
    {
        ImportValidation? accepted = null;
        var request = new PromptDialogRequest(Loc.T("data.importPassword.title"), Loc.T("data.importPassword.action"))
        {
            Subtitle = Path.GetFileName(path),
            First = new PromptDialogField(Loc.T("data.password.label"), Masked: true),
            Validate = async (password, _) =>
            {
                if (password.Length == 0)
                {
                    return Loc.T("data.importPassword.wrong");
                }

                ImportValidation checkedBackup;
                try
                {
                    checkedBackup = await Task.Run(() => _transfer.ValidateImport(path, password));
                }
                catch (Exception ex) when (ex is DatabaseTransferException || IsFileError(ex))
                {
                    return Loc.T("data.import.failed", ("reason", Describe(ex)));
                }

                if (checkedBackup.Error == TransferErrorCode.WrongPassword)
                {
                    checkedBackup.Dispose();
                    return Loc.T("data.importPassword.wrong");
                }

                accepted = checkedBackup;
                return null;
            },
        };

        if (await _prompt.ShowAsync(request))
        {
            return accepted;
        }

        accepted?.Dispose();
        return null;
    }

    private void TrySaveKey(Guid databaseId, DatabaseKey key)
    {
        if (!_keyStore.IsAvailable)
        {
            return;
        }

        try
        {
            _keyStore.Save(databaseId, key);
        }
        catch (DatabaseKeyStoreException)
        {
            // Not fatal: the recovery code is asked for at the next start.
        }
    }

    private async Task ShowExistingAsync(string folder, string targetDb)
    {
        string? error = null;
        var summary = string.Empty;
        try
        {
            var (info, keyFile) = await Task.Run(() => InspectOther(targetDb));
            if (info.KeyProblem is not null && keyFile is not null && _security.Encryption)
            {
                var unlocked = await UnlockOtherAsync(targetDb, keyFile);
                if (unlocked is null)
                {
                    return; // cancelled
                }

                info = unlocked;
            }

            if (info.KeyProblem is not null && !_security.Encryption)
            {
                // An encrypted database in the chosen folder: no recovery code is asked for, this build cannot use it.
                error = Loc.T("transfer.error.encryptionOff");
            }
            else if (info.KeyProblem is not null)
            {
                error = Loc.T("data.existing.unavailable");
            }
            else if (info.IsNewerThanApp)
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

    /// <summary>Inspects the database in another folder with the key from the key store, if its key file and an entry for it exist.</summary>
    private (DatabaseInspection Info, DatabaseKeyFileContents? KeyFile) InspectOther(string databaseFile)
    {
        DatabaseKeyFileContents? keyFile = null;
        try
        {
            keyFile = DatabaseKeyFile.Read(databaseFile);
        }
        catch (DatabaseKeyFileException)
        {
            // A damaged key file is the same as none: the database cannot be opened.
        }

        DatabaseKey? key = null;
        if (keyFile is not null && _security.Encryption && _keyStore.IsAvailable)
        {
            try
            {
                key = _keyStore.Get(keyFile.DatabaseId);
            }
            catch (DatabaseKeyStoreException)
            {
                // An unreadable store is the same as no entry: the recovery code is asked for.
            }
        }

        return (_transfer.Inspect(databaseFile, key), keyFile);
    }

    /// <summary>
    /// Asks for the recovery code of an encrypted database in another folder until it opens the database, then saves the key to the
    /// key store. Returns what was inspected with that key, or null when the user cancelled.
    /// </summary>
    private async Task<DatabaseInspection?> UnlockOtherAsync(string databaseFile, DatabaseKeyFileContents keyFile)
    {
        (DatabaseKey Key, DatabaseInspection Info)? unlocked = null;
        var request = new PromptDialogRequest(Loc.T("recovery.title"), Loc.T("recovery.unlock"))
        {
            Body = Loc.T("recovery.body"),
            Subtitle = Path.GetDirectoryName(databaseFile),
            First = new PromptDialogField(Loc.T("recovery.code"), Mono: true),
            Validate = (code, _) => Task.Run<string?>(() =>
            {
                if (!RecoveryCode.IsValid(code))
                {
                    return Loc.T("recovery.wrong");
                }

                // The key derivation (PBKDF2) is slow on purpose, hence off the UI thread.
                var candidate = DatabaseKeyWrapper.TryUnwrap(keyFile.Wrapped, code);
                if (candidate is null)
                {
                    return Loc.T("recovery.wrong");
                }

                try
                {
                    var info = _transfer.Inspect(databaseFile, candidate);
                    if (info.KeyProblem is not null)
                    {
                        return Loc.T("recovery.wrong");
                    }

                    unlocked = (candidate, info);
                    return null;
                }
                catch (Exception ex) when (ex is DatabaseTransferException or DatabaseOpenException || IsFileError(ex))
                {
                    return Loc.T("data.existing.recoveryFailed");
                }
            }),
        };

        if (!await _prompt.ShowAsync(request) || unlocked is not { } result)
        {
            return null;
        }

        TrySaveKey(keyFile.DatabaseId, result.Key);
        return result.Info;
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
            var previous = _location.Read().Settings.Pending;
            _location.SetPending(step);

            // A staged import that this step replaces will never run, so its copy is removed.
            if (previous is { Kind: PendingKind.Import } old && old.Source != step.Source && Directory.Exists(old.Source))
            {
                _transfer.DiscardStaging(old.Source);
            }

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
