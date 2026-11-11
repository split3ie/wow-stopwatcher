using Microsoft.Data.Sqlite;
using WowStopwatcher.App.Features.Settings;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Tests;

public sealed class DataSettingsViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wowstopwatcher-datavm-" + Guid.NewGuid().ToString("N"));
    private readonly FakePicker _picker = new();
    private readonly FakeDialog _dialog = new();
    private readonly FakeLifetime _lifetime = new();
    private readonly FakeControl _control = new();
    private readonly DatabaseLocation _location;
    private readonly SqliteDatabase _database;
    private readonly DataSettingsViewModel _viewModel;

    public DataSettingsViewModelTests()
    {
        var current = Path.Combine(_root, "current");
        _location = new DatabaseLocation(current);
        _database = new SqliteDatabase(_location.DefaultDatabasePath);
        _viewModel = new DataSettingsViewModel(
            _location, new DatabaseTransfer(TimeProvider.System), _database, _picker, new FakeLauncher(), _dialog, _lifetime, _control, TimeProvider.System);
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
    public async Task Export_WritesBackupFileWithDatedSuggestedName()
    {
        var zip = Path.Combine(_root, "out", "backup.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        _picker.SavePath = zip;

        await _viewModel.ExportCommand.ExecuteAsync(null);

        Assert.True(File.Exists(zip));
        Assert.True(_viewModel.HasInfoStatus);
        Assert.StartsWith("WowStopwatcher-backup-", _picker.SuggestedName);
        Assert.EndsWith(".zip", _picker.SuggestedName);
    }

    [Fact]
    public async Task Export_ToUnwritableLocation_ShowsError()
    {
        var blocker = Path.Combine(_root, "blocker");
        await File.WriteAllTextAsync(blocker, "a file where a folder is needed");
        _picker.SavePath = Path.Combine(blocker, "backup.zip");

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
        Assert.False(_dialog.IsOpen);
        Assert.Null(_location.Read().Settings.Pending);
    }

    [Fact]
    public async Task Import_ValidBackup_ConfirmsThenStopsStopwatchesStoresImportAndRestarts()
    {
        var zip = Path.Combine(_root, "backup.zip");
        new DatabaseTransfer(TimeProvider.System).Export(_database.Path, _location.DefaultFolder, zip);
        _picker.OpenPath = zip;

        await _viewModel.ImportCommand.ExecuteAsync(null);

        Assert.True(_dialog.IsOpen);
        Assert.Null(_location.Read().Settings.Pending);
        Assert.Equal(0, _lifetime.Restarts);

        _dialog.Confirm();

        Assert.Equal(new PendingStep(PendingKind.Import, zip, _database.Path), _location.Read().Settings.Pending);
        Assert.Equal(1, _control.StopAllCalls);
        Assert.Equal(1, _lifetime.Restarts);
    }

    [Fact]
    public async Task Import_ValidBackup_DeclinedChangesNothing()
    {
        var zip = Path.Combine(_root, "backup.zip");
        new DatabaseTransfer(TimeProvider.System).Export(_database.Path, _location.DefaultFolder, zip);
        _picker.OpenPath = zip;

        await _viewModel.ImportCommand.ExecuteAsync(null);
        _dialog.Cancel();

        Assert.Null(_location.Read().Settings.Pending);
        Assert.Equal(0, _control.StopAllCalls);
        Assert.Equal(0, _lifetime.Restarts);
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

        public Task<string?> PickFolderAsync(string title) => Task.FromResult(Folder);

        public Task<string?> PickOpenFileAsync(string title, string filterName, IReadOnlyList<string> extensions) => Task.FromResult(OpenPath);

        public Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension)
        {
            SuggestedName = suggestedName;
            return Task.FromResult(SavePath);
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
