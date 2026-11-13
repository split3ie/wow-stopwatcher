using Microsoft.Data.Sqlite;
using WowStopwatcher.App.Features.Settings;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;
using WowStopwatcher.Core.Security;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Tests;

public sealed class DataSettingsViewModelTests : IDisposable
{
    private const string Password = "correct horse battery";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "wowstopwatcher-datavm-" + Guid.NewGuid().ToString("N"));
    private readonly FakePicker _picker = new();
    private readonly FakeDialog _dialog = new();
    private readonly FakeLifetime _lifetime = new();
    private readonly FakeControl _control = new();
    private readonly FakeKeyStore _keyStore = new();
    private readonly FakeGuard _guard = new();
    private readonly FakePrompt _prompt = new();
    private readonly DatabaseKey _key = DatabaseKey.Generate();
    private readonly DatabaseLocation _location;
    private readonly SqliteDatabase _database;
    private readonly DataSettingsViewModel _viewModel;

    public DataSettingsViewModelTests()
    {
        var current = Path.Combine(_root, "current");
        _location = new DatabaseLocation(current);
        Directory.CreateDirectory(current);
        DatabaseKeyFile.Write(
            _location.DefaultDatabasePath,
            new DatabaseKeyFileContents(Guid.NewGuid(), DatabaseKeyWrapper.Wrap(_key, RecoveryCode.Generate(), iterations: 1000)));
        _database = new SqliteDatabase(_location.DefaultDatabasePath, _key);
        var keys = new DatabaseKeyHolder();
        keys.Set(_key, Guid.NewGuid());
        _viewModel = new DataSettingsViewModel(
            _location, new DatabaseTransfer(TimeProvider.System), _database, _picker, new FakeLauncher(), _dialog, _lifetime, _control,
            TimeProvider.System, keys, _keyStore, _guard, _prompt, SecurityCapabilities.All);
    }

    private string StagingRoot => Path.Combine(_location.DefaultFolder, DatabaseTransfer.StagingFolderName);

    /// <summary>A protected backup of the current database.</summary>
    private string MakeBackup()
    {
        var path = Path.Combine(_root, "backup.wsbackup");
        new DatabaseTransfer(TimeProvider.System).ExportEncrypted(_database.Path, _location.DefaultFolder, path, _key, Password);
        return path;
    }

    /// <summary>An encrypted database in another folder; the recovery code, database id and key are returned.</summary>
    private string CreateEncryptedOther(string name, out string code, out Guid id, out DatabaseKey key)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        var path = Path.Combine(folder, DatabaseLocation.DatabaseFileName);
        code = RecoveryCode.Generate();
        id = Guid.NewGuid();
        key = DatabaseKey.Generate();
        DatabaseKeyFile.Write(path, new DatabaseKeyFileContents(id, DatabaseKeyWrapper.Wrap(key, code, iterations: 1000)));
        _ = new SqliteDatabase(path, key);
        return folder;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup of a temp folder.
        }
    }

    [Fact]
    public async Task ChangeFolder_Cancelled_DoesNothing()
    {
        _picker.Folder = null;

        await _viewModel.ChangeFolderCommand.ExecuteAsync(null);

        Assert.False(_dialog.IsOpen);
        Assert.Null(_location.Read().Settings.Pending);
        Assert.Equal(0, _lifetime.Restarts);
    }

    [Fact]
    public async Task ChangeFolder_CurrentFolder_ShowsMessageOnly()
    {
        _picker.Folder = _location.DefaultFolder;

        await _viewModel.ChangeFolderCommand.ExecuteAsync(null);

        Assert.False(_dialog.IsOpen);
        Assert.True(_viewModel.HasInfoStatus);
        Assert.Null(_location.Read().Settings.Pending);
    }

    [Fact]
    public async Task ChangeFolder_NoDatabaseThere_ConfirmsThenStoresMoveAndRestartsKeepingStopwatches()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;
        _picker.Folder = target;

        await _viewModel.ChangeFolderCommand.ExecuteAsync(null);

        Assert.True(_dialog.IsOpen);
        Assert.Null(_location.Read().Settings.Pending);
        _dialog.Confirm();

        var pending = _location.Read().Settings.Pending;
        Assert.Equal(new PendingStep(PendingKind.Move, _database.Path, Path.Combine(target, DatabaseLocation.DatabaseFileName)), pending);
        Assert.Equal(1, _lifetime.Restarts);
        Assert.Equal(0, _control.StopAllCalls);
    }

    [Fact]
    public async Task ChangeFolder_DatabaseThere_ShowsSummaryWithoutChangingAnything()
    {
        var target = CreateOtherDatabase("other");
        _picker.Folder = target;

        await _viewModel.ChangeFolderCommand.ExecuteAsync(null);

        Assert.True(_viewModel.IsExistingPanelVisible);
        Assert.True(_viewModel.CanUseExisting);
        Assert.False(string.IsNullOrEmpty(_viewModel.ExistingSummary));
        Assert.False(_dialog.IsOpen);
        Assert.Null(_location.Read().Settings.Pending);
        Assert.Equal(0, _lifetime.Restarts);
    }

    [Fact]
    public async Task UseExisting_SwitchesFolderStopsStopwatchesAndRestarts()
    {
        var target = CreateOtherDatabase("other");
        _picker.Folder = target;
        await _viewModel.ChangeFolderCommand.ExecuteAsync(null);

        _viewModel.UseExistingCommand.Execute(null);

        Assert.Equal(Path.GetFullPath(target), _location.Read().Settings.DatabaseFolder);
        Assert.Null(_location.Read().Settings.Pending);
        Assert.Equal(1, _control.StopAllCalls);
        Assert.Equal(1, _lifetime.Restarts);
    }

    [Fact]
    public async Task ReplaceWithMine_StoresReplaceAndRestartsKeepingStopwatches()
    {
        var target = CreateOtherDatabase("other");
        _picker.Folder = target;
        await _viewModel.ChangeFolderCommand.ExecuteAsync(null);

        _viewModel.ReplaceWithMineCommand.Execute(null);

        Assert.Equal(
            new PendingStep(PendingKind.Replace, _database.Path, Path.Combine(target, DatabaseLocation.DatabaseFileName)),
            _location.Read().Settings.Pending);
        Assert.Equal(0, _control.StopAllCalls);
        Assert.Equal(1, _lifetime.Restarts);
    }

    [Fact]
    public async Task ChangeFolder_NewerDatabaseThere_CannotBeUsedAndNothingChanges()
    {
        var target = CreateOtherDatabase("newer");
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(target, DatabaseLocation.DatabaseFileName)};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {SqliteDatabase.LatestSchemaVersion + 1};";
            command.ExecuteNonQuery();
        }

        _picker.Folder = target;
        await _viewModel.ChangeFolderCommand.ExecuteAsync(null);
        _viewModel.UseExistingCommand.Execute(null);

        Assert.True(_viewModel.IsExistingPanelVisible);
        Assert.False(_viewModel.CanUseExisting);
        Assert.True(_viewModel.HasExistingError);
        Assert.Null(_location.Read().Settings.DatabaseFolder);
        Assert.Equal(0, _lifetime.Restarts);
    }

    [Fact]
    public async Task CancelExisting_HidesPanel()
    {
        _picker.Folder = CreateOtherDatabase("other");
        await _viewModel.ChangeFolderCommand.ExecuteAsync(null);

        _viewModel.CancelExistingCommand.Execute(null);

        Assert.False(_viewModel.IsExistingPanelVisible);
        Assert.Equal(0, _lifetime.Restarts);
    }

    [Fact]
    public async Task Export_AsksPasswordThenWritesProtectedBackupWithDatedName()
    {
        var path = Path.Combine(_root, "out", "backup.wsbackup");
        _picker.SavePath = path;
        _prompt.Script((Password, Password));

        await _viewModel.ExportCommand.ExecuteAsync(null);

        Assert.True(File.Exists(path));
        Assert.True(_viewModel.HasInfoStatus);
        Assert.StartsWith("WowStopwatcher-backup-", _picker.SuggestedName);
        Assert.EndsWith(".wsbackup", _picker.SuggestedName);
        Assert.Equal("wsbackup", _picker.SaveExtension);
        using var check = new DatabaseTransfer(TimeProvider.System).ValidateImport(path, Password);
        Assert.True(check.IsValid);
        Assert.True(_key.KeyEquals(check.Key));
    }

    // Build without encryption

    /// <summary>A view model of a build without encryption, over a plain database in its own folder.</summary>
    private (DataSettingsViewModel ViewModel, SqliteDatabase Database, DatabaseLocation Location) CreatePlainViewModel()
    {
        var location = new DatabaseLocation(Path.Combine(_root, "plain-current"));
        var database = new SqliteDatabase(location.DefaultDatabasePath);
        var viewModel = new DataSettingsViewModel(
            location, new DatabaseTransfer(TimeProvider.System), database, _picker, new FakeLauncher(), _dialog, _lifetime, _control,
            TimeProvider.System, new DatabaseKeyHolder(), _keyStore, _guard, _prompt, new SecurityCapabilities(false));
        return (viewModel, database, location);
    }

    [Fact]
    public async Task EncryptionOff_Export_WritesAPlainZipWithoutAPasswordDialog()
    {
        var (viewModel, _, _) = CreatePlainViewModel();
        var path = Path.Combine(_root, "out", "backup.zip");
        _picker.SavePath = path;

        await viewModel.ExportCommand.ExecuteAsync(null);

        Assert.True(File.Exists(path));
        Assert.True(viewModel.HasInfoStatus);
        Assert.EndsWith(".zip", _picker.SuggestedName);
        Assert.Equal("zip", _picker.SaveExtension);
        Assert.Empty(_prompt.Requests);
        using var check = new DatabaseTransfer(TimeProvider.System).ValidateImport(path);
        Assert.True(check.IsValid);
        Assert.Null(check.Key);
    }

    [Fact]
    public async Task EncryptionOff_Import_RefusesAProtectedBackupAndChangesNothing()
    {
        var backup = MakeBackup();
        var (viewModel, database, location) = CreatePlainViewModel();
        _picker.OpenPath = backup;

        await viewModel.ImportCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasErrorStatus);
        Assert.Equal(Loc.T("data.import.failed", ("reason", Loc.T("transfer.error.encryptionOff"))), viewModel.StatusText);
        Assert.Null(location.Read().Settings.Pending);
        Assert.False(Directory.Exists(Path.Combine(location.DefaultFolder, DatabaseTransfer.StagingFolderName)));
        Assert.Equal(0, _lifetime.Restarts);
        Assert.Equal(DatabaseFileFormat.Plain, DatabaseFormatDetector.Detect(database.Path));
    }

    [Fact]
    public async Task EncryptionOff_ChangeFolder_EncryptedDatabaseThere_IsRefusedWithoutAskingForACode()
    {
        var folder = CreateEncryptedOther("other-encrypted", out _, out _, out _);
        var (viewModel, _, _) = CreatePlainViewModel();
        _picker.Folder = folder;

        await viewModel.ChangeFolderCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsExistingPanelVisible);
        Assert.False(viewModel.CanUseExisting);
        Assert.Equal(Loc.T("transfer.error.encryptionOff"), viewModel.ExistingError);
        Assert.Empty(_prompt.Requests);
        Assert.Empty(_keyStore.Keys);
    }

    [Fact]
    public async Task Export_EmptyPassword_IsRefusedAndNothingIsWritten()
    {
        _picker.SavePath = Path.Combine(_root, "out", "backup.wsbackup");
        _prompt.Script(("", ""));

        await _viewModel.ExportCommand.ExecuteAsync(null);

        Assert.Single(_prompt.Errors);
        Assert.Null(_picker.SuggestedName);
        Assert.False(File.Exists(_picker.SavePath));
    }

    [Fact]
    public async Task Export_TooShortPassword_IsRefusedUntilLongEnough()
    {
        _picker.SavePath = Path.Combine(_root, "out", "backup.wsbackup");
        _prompt.Script(("short", "short"), (Password, Password));

        await _viewModel.ExportCommand.ExecuteAsync(null);

        Assert.Single(_prompt.Errors);
        Assert.True(File.Exists(_picker.SavePath));
    }

    [Fact]
    public async Task Export_MismatchedPasswords_AreRefusedUntilTheyMatch()
    {
        var path = Path.Combine(_root, "out", "backup.wsbackup");
        _picker.SavePath = path;
        _prompt.Script(("one", "two"), (Password, Password));

        await _viewModel.ExportCommand.ExecuteAsync(null);

        Assert.Single(_prompt.Errors);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Export_PasswordDialogCancelled_DoesNothing()
    {
        _picker.SavePath = Path.Combine(_root, "out", "backup.wsbackup");
        _prompt.Script();

        await _viewModel.ExportCommand.ExecuteAsync(null);

        Assert.Null(_picker.SuggestedName);
        Assert.False(_viewModel.HasInfoStatus);
        Assert.False(_viewModel.HasErrorStatus);
    }

    [Fact]
    public async Task Export_ToUnwritableLocation_ShowsError()
    {
        var blocker = Path.Combine(_root, "blocker");
        await File.WriteAllTextAsync(blocker, "a file where a folder is needed");
        _picker.SavePath = Path.Combine(blocker, "backup.wsbackup");
        _prompt.Script((Password, Password));

        await _viewModel.ExportCommand.ExecuteAsync(null);

        Assert.True(_viewModel.HasErrorStatus);
        Assert.False(File.Exists(_picker.SavePath));
    }

    [Fact]
    public async Task Import_NotABackup_ShowsReasonAndDoesNotAsk()
    {
        var file = Path.Combine(_root, "notes.zip");
        await File.WriteAllTextAsync(file, "this is not a zip");
        _picker.OpenPath = file;

        await _viewModel.ImportCommand.ExecuteAsync(null);

        Assert.True(_viewModel.HasErrorStatus);
        Assert.Empty(_prompt.Requests);
        Assert.Null(_location.Read().Settings.Pending);
    }

    [Fact]
    public async Task Import_ProtectedBackup_WrongThenRightPassword_StagesSavesKeyAndStoresPending()
    {
        var backup = MakeBackup();
        _picker.OpenPath = backup;
        _prompt.Script(("wrong", ""), (Password, ""));
        _prompt.Script(("", ""));

        await _viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Single(_prompt.Errors);
        Assert.Equal(2, _prompt.Requests.Count);
        Assert.True(_prompt.Requests[1].IsDestructive);
        var id = DatabaseKeyFile.Read(_database.Path)!.DatabaseId;
        Assert.True(_key.KeyEquals(_keyStore.Keys[id]));
        var pending = _location.Read().Settings.Pending;
        Assert.NotNull(pending);
        Assert.Equal(PendingKind.Import, pending.Kind);
        Assert.Equal(_database.Path, pending.Target);
        Assert.Equal(DatabaseTransfer.StagingFolderName, Path.GetFileName(Path.GetDirectoryName(pending.Source)));
        Assert.True(File.Exists(Path.Combine(pending.Source, DatabaseLocation.DatabaseFileName)));
        Assert.Equal(1, _control.StopAllCalls);
        Assert.Equal(1, _lifetime.Restarts);
    }

    [Fact]
    public async Task Import_ProtectedBackup_PasswordDialogCancelled_ChangesNothing()
    {
        _picker.OpenPath = MakeBackup();
        _prompt.Script(("wrong", ""));

        await _viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Single(_prompt.Errors);
        Assert.Single(_prompt.Requests);
        Assert.Empty(_keyStore.Keys);
        Assert.Null(_location.Read().Settings.Pending);
        Assert.False(Directory.Exists(StagingRoot));
        Assert.Equal(0, _lifetime.Restarts);
    }

    [Fact]
    public async Task Import_ProtectedBackup_ConfirmDeclined_DiscardsStagingAndSavesNoKey()
    {
        _picker.OpenPath = MakeBackup();
        _prompt.Script((Password, ""));

        await _viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Equal(2, _prompt.Requests.Count);
        Assert.Empty(_keyStore.Keys);
        Assert.Null(_location.Read().Settings.Pending);
        Assert.False(Directory.Exists(StagingRoot));
        Assert.Equal(0, _control.StopAllCalls);
        Assert.Equal(0, _lifetime.Restarts);
    }

    [Fact]
    public async Task Import_ProtectedBackup_NoKeyStore_StillStagesAndStoresPending()
    {
        _keyStore.IsAvailable = false;
        _picker.OpenPath = MakeBackup();
        _prompt.Script((Password, ""));
        _prompt.Script(("", ""));

        await _viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Equal(PendingKind.Import, _location.Read().Settings.Pending?.Kind);
        Assert.Equal(1, _lifetime.Restarts);
    }

    [Fact]
    public async Task StagedImport_ReplacedByAnotherPendingStep_IsDiscarded()
    {
        _picker.OpenPath = MakeBackup();
        _prompt.Script((Password, ""));
        _prompt.Script(("", ""));
        await _viewModel.ImportCommand.ExecuteAsync(null);
        var staged = _location.Read().Settings.Pending!.Source;
        Assert.True(Directory.Exists(staged));

        _picker.Folder = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;
        await _viewModel.ChangeFolderCommand.ExecuteAsync(null);
        _dialog.Confirm();

        Assert.Equal(PendingKind.Move, _location.Read().Settings.Pending?.Kind);
        Assert.False(Directory.Exists(staged));
    }

    [Fact]
    public async Task Import_OldZip_AsksOnlyForConfirmationAndStoresZipAsSource()
    {
        var zip = Path.Combine(_root, "old.zip");
        var plainFolder = CreateOtherDatabase("plain");
        new DatabaseTransfer(TimeProvider.System).Export(Path.Combine(plainFolder, DatabaseLocation.DatabaseFileName), plainFolder, zip);
        _picker.OpenPath = zip;
        _prompt.Script(("", ""));

        await _viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Single(_prompt.Requests);
        Assert.Equal(new PendingStep(PendingKind.Import, zip, _database.Path), _location.Read().Settings.Pending);
        Assert.Empty(_keyStore.Keys);
        Assert.Equal(1, _control.StopAllCalls);
        Assert.Equal(1, _lifetime.Restarts);
    }

    [Fact]
    public async Task Import_OldZip_DeclinedChangesNothing()
    {
        var zip = Path.Combine(_root, "old.zip");
        var plainFolder = CreateOtherDatabase("plain");
        new DatabaseTransfer(TimeProvider.System).Export(Path.Combine(plainFolder, DatabaseLocation.DatabaseFileName), plainFolder, zip);
        _picker.OpenPath = zip;
        _prompt.Script();

        await _viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Null(_location.Read().Settings.Pending);
        Assert.Equal(0, _control.StopAllCalls);
        Assert.Equal(0, _lifetime.Restarts);
    }

    [Fact]
    public async Task ChangeFolder_EncryptedDatabaseThere_RightRecoveryCodeSavesKeyAndShowsSummary()
    {
        var other = CreateEncryptedOther("enc", out var code, out var id, out var key);
        _picker.Folder = other;
        _prompt.Script((RecoveryCode.Generate(), ""), (code, ""));

        await _viewModel.ChangeFolderCommand.ExecuteAsync(null);

        Assert.Single(_prompt.Errors);
        Assert.True(_viewModel.IsExistingPanelVisible);
        Assert.True(_viewModel.CanUseExisting);
        Assert.True(key.KeyEquals(_keyStore.Keys[id]));
        Assert.Null(_location.Read().Settings.Pending);
    }

    [Fact]
    public async Task ChangeFolder_EncryptedDatabaseThere_KeyInStore_AsksNothing()
    {
        var other = CreateEncryptedOther("enc", out _, out var id, out var key);
        _keyStore.Keys[id] = key;
        _picker.Folder = other;

        await _viewModel.ChangeFolderCommand.ExecuteAsync(null);

        Assert.Empty(_prompt.Requests);
        Assert.True(_viewModel.IsExistingPanelVisible);
        Assert.True(_viewModel.CanUseExisting);
    }

    [Fact]
    public async Task ChangeFolder_EncryptedDatabaseThere_WrongCodeThenCancel_ShowsNothingAndSavesNoKey()
    {
        var other = CreateEncryptedOther("enc", out _, out _, out _);
        _picker.Folder = other;
        _prompt.Script((RecoveryCode.Generate(), ""));

        await _viewModel.ChangeFolderCommand.ExecuteAsync(null);

        Assert.Single(_prompt.Errors);
        Assert.False(_viewModel.IsExistingPanelVisible);
        Assert.Empty(_keyStore.Keys);
        Assert.Null(_location.Read().Settings.Pending);
    }

    [Fact]
    public async Task ChangeFolder_EncryptedDatabaseThere_ReplaceWithMineStillStoresReplace()
    {
        var other = CreateEncryptedOther("enc", out var code, out _, out _);
        _picker.Folder = other;
        _prompt.Script((code, ""));
        await _viewModel.ChangeFolderCommand.ExecuteAsync(null);

        _viewModel.ReplaceWithMineCommand.Execute(null);

        Assert.Equal(PendingKind.Replace, _location.Read().Settings.Pending?.Kind);
        Assert.Equal(1, _lifetime.Restarts);
    }

    [Fact]
    public async Task Guard_Refusing_BlocksEveryAction()
    {
        _guard.Allow = false;
        _picker.Folder = CreateOtherDatabase("other");
        _picker.OpenPath = MakeBackup();
        _picker.SavePath = Path.Combine(_root, "out", "backup.wsbackup");

        await _viewModel.ExportCommand.ExecuteAsync(null);
        await _viewModel.ImportCommand.ExecuteAsync(null);
        await _viewModel.ChangeFolderCommand.ExecuteAsync(null);

        Assert.Equal(3, _guard.Calls);
        Assert.Empty(_prompt.Requests);
        Assert.Equal(0, _picker.Picks);
        Assert.False(_dialog.IsOpen);
        Assert.False(_viewModel.IsExistingPanelVisible);
        Assert.Null(_location.Read().Settings.Pending);
    }

    [Fact]
    public async Task CannotRestart_QuitsInstead()
    {
        _lifetime.CanRestart = false;
        var target = CreateOtherDatabase("other");
        _picker.Folder = target;
        await _viewModel.ChangeFolderCommand.ExecuteAsync(null);

        _viewModel.UseExistingCommand.Execute(null);

        Assert.Equal(0, _lifetime.Restarts);
        Assert.Equal(1, _lifetime.Quits);
    }

    private string CreateOtherDatabase(string name)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        _ = new SqliteDatabase(Path.Combine(folder, DatabaseLocation.DatabaseFileName));
        return folder;
    }

    private sealed class FakePicker : IFilePickerService
    {
        public string? Folder { get; set; }

        public string? OpenPath { get; set; }

        public string? SavePath { get; set; }

        public string? SuggestedName { get; private set; }

        public string? SaveExtension { get; private set; }

        public int Picks { get; private set; }

        public Task<string?> PickFolderAsync(string title)
        {
            Picks++;
            return Task.FromResult(Folder);
        }

        public Task<string?> PickOpenFileAsync(string title, string filterName, IReadOnlyList<string> extensions)
        {
            Picks++;
            return Task.FromResult(OpenPath);
        }

        public Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension)
        {
            Picks++;
            SuggestedName = suggestedName;
            SaveExtension = extension;
            return Task.FromResult(SavePath);
        }
    }

    private sealed class FakeKeyStore : IDatabaseKeyStore
    {
        public bool IsAvailable { get; set; } = true;

        public Dictionary<Guid, DatabaseKey> Keys { get; } = [];

        public DatabaseKey? Get(Guid databaseId) => Keys.GetValueOrDefault(databaseId);

        public void Save(Guid databaseId, DatabaseKey key)
        {
            if (!IsAvailable)
            {
                throw new DatabaseKeyStoreException("The store is not usable.");
            }

            Keys[databaseId] = key;
        }

        public void Delete(Guid databaseId) => Keys.Remove(databaseId);
    }

    private sealed class FakeGuard : ISensitiveActionGuard
    {
        public bool Allow { get; set; } = true;

        public int Calls { get; private set; }

        public Task<bool> ConfirmAsync()
        {
            Calls++;
            return Task.FromResult(Allow);
        }
    }

    /// <summary>Answers each dialog from a script: the field texts typed in turn until one is accepted; none left means the user cancels.</summary>
    private sealed class FakePrompt : IPromptDialogService
    {
        private readonly Queue<(string First, string Second)[]> _scripts = new();

        public List<PromptDialogRequest> Requests { get; } = [];

        public List<string> Errors { get; } = [];

        public void Script(params (string First, string Second)[] attempts) => _scripts.Enqueue(attempts);

        public async Task<bool> ShowAsync(PromptDialogRequest request)
        {
            Requests.Add(request);
            var attempts = _scripts.Count > 0 ? _scripts.Dequeue() : [];
            foreach (var (first, second) in attempts)
            {
                var error = request.Validate is null ? null : await request.Validate(first, second);
                if (error is null)
                {
                    return true;
                }

                Errors.Add(error);
            }

            return false;
        }
    }

    private sealed class FakeLauncher : IFolderLauncher
    {
        public Task<bool> OpenAsync(string path) => Task.FromResult(true);
    }

    private sealed class FakeDialog : IDialogService
    {
        private Action? _onConfirm;

        public bool IsOpen { get; private set; }

        public string Body { get; set; } = string.Empty;

        public void Show(string title, string body, string confirmLabel, Action onConfirm)
        {
            Body = body;
            _onConfirm = onConfirm;
            IsOpen = true;
        }

        public void Cancel()
        {
            _onConfirm = null;
            IsOpen = false;
        }

        public void Confirm()
        {
            var action = _onConfirm;
            Cancel();
            action?.Invoke();
        }
    }

    private sealed class FakeLifetime : IAppLifetime
    {
        public int Restarts { get; private set; }

        public int Quits { get; private set; }

        public bool KeepsRunningSessions { get; private set; }

        public bool CanRestart { get; set; } = true;

        public void Quit() => Quits++;

        public void QuitKeepingRunningSessions()
        {
            KeepsRunningSessions = true;
            Quits++;
        }

        public void Restart() => Restarts++;
    }

    private sealed class FakeControl : IStopwatchControl
    {
        public int StopAllCalls { get; private set; }

        public void Start(string id)
        {
        }

        public void Toggle(string id)
        {
        }

        public void StopAll() => StopAllCalls++;

        public void StartNext()
        {
        }
    }
}
