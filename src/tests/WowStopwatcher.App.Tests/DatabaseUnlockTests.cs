using Microsoft.Data.Sqlite;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Security;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Tests;

public sealed class DatabaseUnlockTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wowstopwatcher-unlock-" + Guid.NewGuid().ToString("N"));
    private readonly DatabaseLocation _location;
    private readonly FakeKeyStore _store = new();
    private readonly FakePrompts _prompts = new();
    private readonly RecordingLanguage _language = new();
    private SecurityCapabilities _security = SecurityCapabilities.All;
    private readonly ShellOptions _shell = new();

    public DatabaseUnlockTests()
    {
        _location = new DatabaseLocation(Path.Combine(_root, "default"));
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

    private string DbPath => _location.DefaultDatabasePath;

    private DatabaseUnlock CreateUnlock()
    {
        var transfer = new DatabaseTransfer(new FixedTimeProvider(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero)));
        return new DatabaseUnlock(_location, new DatabaseStartup(_location, transfer), _store, _shell, _prompts, _language, _security);
    }

    private static (DatabaseKey Key, Guid Id, string Code) CreateEncrypted(string path, string? language = null)
    {
        var key = DatabaseKey.Generate();
        var id = Guid.NewGuid();
        var code = RecoveryCode.Generate();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        DatabaseKeyFile.Write(path, new DatabaseKeyFileContents(id, DatabaseKeyWrapper.Wrap(key, code, iterations: 1000)));
        var database = new SqliteDatabase(path, key);
        if (language is not null)
        {
            new SqliteSettingsRepository(database).Set("language", language);
        }

        return (key, id, code);
    }

    private static void CreatePlain(string path, string language)
    {
        new SqliteSettingsRepository(new SqliteDatabase(path)).Set("language", language);
    }

    private static Func<ProtectDataRequest, Task<ProtectDataOutcome>> Confirm(string? pin = null) => async request =>
    {
        await request.Commit(pin);
        return ProtectDataOutcome.Done;
    };

    [Fact]
    public async Task NewInstall_CreatesEncryptedDatabaseKeyFileAndStoreEntry()
    {
        _prompts.OnProtect = Confirm();

        var result = await CreateUnlock().RunAsync();

        Assert.NotNull(result);
        var request = Assert.Single(_prompts.ProtectRequests);
        Assert.True(request.IsNewDatabase);
        Assert.False(request.PinAvailable);
        Assert.True(RecoveryCode.IsValid(request.RecoveryCode));
        Assert.Null(result.PinChoice);
        Assert.Equal(DatabaseFileFormat.Encrypted, DatabaseFormatDetector.Detect(DbPath));
        var file = DatabaseKeyFile.Read(DbPath);
        Assert.NotNull(file);
        Assert.Equal(file.DatabaseId, result.DatabaseId);
        Assert.True(result.Key!.KeyEquals(_store.Keys[file.DatabaseId]));
        Assert.True(result.Key.KeyEquals(DatabaseKeyWrapper.TryUnwrap(file.Wrapped, request.RecoveryCode)));
        Assert.True(DatabaseEncryption.KeyOpens(DbPath, result.Key));
        Assert.Equal(new string?[] { null }, _language.Applied);
    }

    [Fact]
    public async Task NewInstall_WithPinSetup_PassesTheChosenPin()
    {
        _shell.StartupPinStep = true;
        _prompts.OnProtect = Confirm("258012");

        var result = await CreateUnlock().RunAsync();

        Assert.True(Assert.Single(_prompts.ProtectRequests).PinAvailable);
        Assert.Equal("258012", result!.PinChoice!.Pin);
    }

    [Fact]
    public async Task NewInstall_WithPinSetupAndNoPin_ReportsTheChoice()
    {
        _shell.StartupPinStep = true;
        _prompts.OnProtect = Confirm(null);

        var result = await CreateUnlock().RunAsync();

        Assert.NotNull(result!.PinChoice);
        Assert.Null(result.PinChoice.Pin);
    }

    [Fact]
    public async Task NewInstall_WithoutKeyStore_StillCreatesTheDatabase()
    {
        _store.IsAvailable = false;
        _prompts.OnProtect = Confirm();

        var result = await CreateUnlock().RunAsync();

        Assert.NotNull(result);
        Assert.Empty(_store.Keys);
        Assert.True(DatabaseEncryption.KeyOpens(DbPath, result.Key!));
    }

    [Fact]
    public async Task NewInstall_ClosedWindow_CreatesNothing()
    {
        _prompts.OnProtect = _ => Task.FromResult(ProtectDataOutcome.Cancelled);

        var result = await CreateUnlock().RunAsync();

        Assert.Null(result);
        Assert.False(File.Exists(DbPath));
        Assert.Empty(_store.Keys);
    }

    [Fact]
    public async Task PlainDatabase_IsEncryptedInPlaceAndKeepsItsData()
    {
        CreatePlain(DbPath, "uk");
        _prompts.OnProtect = Confirm();

        var result = await CreateUnlock().RunAsync();

        Assert.NotNull(result);
        Assert.False(Assert.Single(_prompts.ProtectRequests).IsNewDatabase);
        Assert.Equal(new string?[] { "uk" }, _language.Applied);
        Assert.Equal(DatabaseFileFormat.Encrypted, DatabaseFormatDetector.Detect(DbPath));
        var reopened = new SqliteDatabase(DbPath, result.Key!);
        Assert.Equal("uk", new SqliteSettingsRepository(reopened).Get("language"));
        Assert.True(result.Key!.KeyEquals(_store.Keys[result.DatabaseId!.Value]));
    }

    [Fact]
    public async Task PlainDatabase_ClosedWindow_LeavesItUntouched()
    {
        CreatePlain(DbPath, "uk");
        SqliteConnection.ClearAllPools();
        var before = File.ReadAllBytes(DbPath);
        _prompts.OnProtect = _ => Task.FromResult(ProtectDataOutcome.Cancelled);

        var result = await CreateUnlock().RunAsync();

        Assert.Null(result);
        Assert.Equal(before, File.ReadAllBytes(DbPath));
        Assert.False(File.Exists(DatabaseKeyFile.PathFor(DbPath)));
        Assert.Empty(_store.Keys);
    }

    [Fact]
    public async Task PlainDatabase_FailedEncryption_ThrowsAndKeepsTheOriginal()
    {
        CreatePlain(DbPath, "uk");
        SqliteConnection.ClearAllPools();
        var before = File.ReadAllBytes(DbPath);
        // A folder where the key file must go makes the first step fail.
        Directory.CreateDirectory(DatabaseKeyFile.PathFor(DbPath));
        _prompts.OnProtect = Confirm();

        var ex = await Assert.ThrowsAsync<DatabaseOpenException>(() => CreateUnlock().RunAsync());

        Assert.Equal(DatabaseProblem.EncryptionFailed, ex.Problem);
        Assert.Equal(DatabaseFileFormat.Plain, DatabaseFormatDetector.Detect(DbPath));
        Assert.Equal(before, File.ReadAllBytes(DbPath));
        Assert.Empty(_store.Keys);
    }

    [Fact]
    public async Task EncryptedDatabase_WithKeyInStore_OpensWithoutAWindow()
    {
        var (key, id, _) = CreateEncrypted(DbPath);
        _store.Keys[id] = key;

        var result = await CreateUnlock().RunAsync();

        Assert.NotNull(result);
        Assert.True(key.KeyEquals(result.Key));
        Assert.Equal(id, result.DatabaseId);
        Assert.Empty(_prompts.ProtectRequests);
        Assert.Empty(_prompts.RecoveryRequests);
        Assert.Empty(_language.Applied);
    }

    [Fact]
    public async Task EncryptedDatabase_KeyMissingInStore_AsksForTheCodeAndSavesTheKey()
    {
        var (key, id, code) = CreateEncrypted(DbPath);
        bool? wrongAccepted = null;
        bool? rightAccepted = null;
        _prompts.OnRecovery = async request =>
        {
            wrongAccepted = await request.TryCode("7KQ2-M9XD-4TPA-HV3C-R8NE-W6JA");
            rightAccepted = await request.TryCode(code.ToLowerInvariant().Replace("-", " "));
            return RecoveryPromptOutcome.Unlocked;
        };

        var result = await CreateUnlock().RunAsync();

        Assert.False(wrongAccepted);
        Assert.True(rightAccepted);
        Assert.Equal(DbPath, Assert.Single(_prompts.RecoveryRequests).DatabasePath);
        Assert.True(key.KeyEquals(result!.Key));
        Assert.True(key.KeyEquals(_store.Keys[id]));
        Assert.Equal(new string?[] { null }, _language.Applied);
    }

    [Fact]
    public async Task EncryptedDatabase_WrongCodeThenQuit_ReturnsNull()
    {
        var (_, id, _) = CreateEncrypted(DbPath);
        _prompts.OnRecovery = async request =>
        {
            Assert.False(await request.TryCode("not a code"));
            Assert.False(await request.TryCode("7KQ2-M9XD-4TPA-HV3C-R8NE-W6JA"));
            return RecoveryPromptOutcome.Quit;
        };

        var result = await CreateUnlock().RunAsync();

        Assert.Null(result);
        Assert.False(_store.Keys.ContainsKey(id));
    }

    [Fact]
    public async Task EncryptedDatabase_StoreUnavailable_AsksForTheCodeAndOpens()
    {
        var (key, _, code) = CreateEncrypted(DbPath);
        _store.IsAvailable = false;
        _prompts.OnRecovery = async request =>
            await request.TryCode(code) ? RecoveryPromptOutcome.Unlocked : RecoveryPromptOutcome.Quit;

        var result = await CreateUnlock().RunAsync();

        Assert.True(key.KeyEquals(result!.Key));
        Assert.Empty(_store.Keys);
    }

    [Fact]
    public async Task EncryptedDatabase_StoreFailsToSave_StillOpens()
    {
        var (key, _, code) = CreateEncrypted(DbPath);
        _store.ThrowOnSave = true;
        _prompts.OnRecovery = async request =>
            await request.TryCode(code) ? RecoveryPromptOutcome.Unlocked : RecoveryPromptOutcome.Quit;

        var result = await CreateUnlock().RunAsync();

        Assert.True(key.KeyEquals(result!.Key));
    }

    [Fact]
    public async Task EncryptedDatabase_RejectedStoredKey_AsksForTheCodeAndReplacesTheEntry()
    {
        var (key, id, code) = CreateEncrypted(DbPath);
        _store.Keys[id] = DatabaseKey.Generate();
        _prompts.OnRecovery = async request =>
            await request.TryCode(code) ? RecoveryPromptOutcome.Unlocked : RecoveryPromptOutcome.Quit;

        var result = await CreateUnlock().RunAsync();

        Assert.Single(_prompts.RecoveryRequests);
        Assert.True(key.KeyEquals(result!.Key));
        Assert.True(key.KeyEquals(_store.Keys[id]));
    }

    [Fact]
    public async Task EncryptedDatabase_KeyFileMissing_ThrowsWithoutAWindow()
    {
        var (_, _, _) = CreateEncrypted(DbPath);
        File.Delete(DatabaseKeyFile.PathFor(DbPath));

        var ex = await Assert.ThrowsAsync<DatabaseOpenException>(() => CreateUnlock().RunAsync());

        Assert.Equal(DatabaseProblem.KeyFileMissing, ex.Problem);
        Assert.Equal(DatabaseKeyFile.PathFor(DbPath), ex.Path);
        Assert.Empty(_prompts.RecoveryRequests);
        Assert.True(File.Exists(DbPath));
    }

    [Fact]
    public async Task EncryptedDatabase_KeyFileDamaged_ThrowsWithoutAWindow()
    {
        var (_, _, _) = CreateEncrypted(DbPath);
        File.WriteAllText(DatabaseKeyFile.PathFor(DbPath), "{ not json");

        var ex = await Assert.ThrowsAsync<DatabaseOpenException>(() => CreateUnlock().RunAsync());

        Assert.Equal(DatabaseProblem.KeyFileDamaged, ex.Problem);
        Assert.Empty(_prompts.RecoveryRequests);
    }

    [Fact]
    public async Task InterruptedEncryption_UnexpectedLeftovers_ThrowConflictAndTouchNothing()
    {
        CreatePlain(DbPath, "uk");
        SqliteConnection.ClearAllPools();
        var aside = DbPath + DatabaseEncryption.AsideSuffix;
        File.WriteAllText(aside, "leftover");

        var ex = await Assert.ThrowsAsync<DatabaseOpenException>(() => CreateUnlock().RunAsync());

        Assert.Equal(DatabaseProblem.EncryptionConflict, ex.Problem);
        Assert.Equal("leftover", File.ReadAllText(aside));
        Assert.Equal(DatabaseFileFormat.Plain, DatabaseFormatDetector.Detect(DbPath));
        Assert.Empty(_prompts.ProtectRequests);
    }

    [Fact]
    public async Task InterruptedEncryption_RollbackLeftovers_AreRemovedBeforeProtecting()
    {
        CreatePlain(DbPath, "uk");
        SqliteConnection.ClearAllPools();
        var temp = DbPath + DatabaseEncryption.TempSuffix;
        File.WriteAllText(temp, "half written");
        _prompts.OnProtect = _ => Task.FromResult(ProtectDataOutcome.Cancelled);

        var result = await CreateUnlock().RunAsync();

        Assert.Null(result);
        Assert.False(File.Exists(temp));
        Assert.Equal(DatabaseFileFormat.Plain, DatabaseFormatDetector.Detect(DbPath));
        Assert.Single(_prompts.ProtectRequests);
    }

    [Fact]
    public async Task PendingMove_UsesTheCurrentKeyAndOpensTheMovedDatabaseSilently()
    {
        var (key, id, _) = CreateEncrypted(DbPath, "uk");
        _store.Keys[id] = key;
        var target = Path.Combine(_root, "moved", DatabaseLocation.DatabaseFileName);
        _location.SetPending(new PendingStep(PendingKind.Move, DbPath, target));

        var result = await CreateUnlock().RunAsync();

        Assert.NotNull(result);
        Assert.NotNull(result.PendingOutcome);
        Assert.True(result.PendingOutcome.Success, result.PendingOutcome.Message);
        Assert.Equal(target, _location.ResolveDatabasePath());
        Assert.True(File.Exists(target));
        Assert.True(File.Exists(DatabaseKeyFile.PathFor(target)));
        Assert.True(DatabaseEncryption.KeyOpens(target, result.Key!));
        Assert.True(key.KeyEquals(result.Key));
        Assert.Empty(_prompts.RecoveryRequests);
    }

    [Fact]
    public async Task PendingMove_ToAFolderWithAnotherEncryptedDatabase_AsksForThatDatabasesCode()
    {
        var (_, id, _) = CreateEncrypted(DbPath);
        _store.Keys[id] = DatabaseKey.Generate();
        var other = Path.Combine(_root, "other", DatabaseLocation.DatabaseFileName);
        var (otherKey, _, otherCode) = CreateEncrypted(other);
        SqliteConnection.ClearAllPools();
        _location.SetFolder(Path.GetDirectoryName(other));
        _prompts.OnRecovery = async request =>
            await request.TryCode(otherCode) ? RecoveryPromptOutcome.Unlocked : RecoveryPromptOutcome.Quit;

        var result = await CreateUnlock().RunAsync();

        Assert.Equal(other, Assert.Single(_prompts.RecoveryRequests).DatabasePath);
        Assert.True(otherKey.KeyEquals(result!.Key));
    }

    [Fact]
    public async Task MissingConfiguredFolder_ThrowsBeforeAnyWindow()
    {
        _location.SetFolder(Path.Combine(_root, "unplugged"));

        var ex = await Assert.ThrowsAsync<DatabaseOpenException>(() => CreateUnlock().RunAsync());

        Assert.Equal(DatabaseProblem.FolderMissing, ex.Problem);
        Assert.Empty(_prompts.ProtectRequests);
        Assert.False(Directory.Exists(Path.Combine(_root, "unplugged")));
    }

    [Fact]
    public void NullStartupPinSetup_AppliesNothing()
    {
        new NullStartupPinSetup().Apply("1234");
    }

    // Build without encryption

    private async Task<DatabaseOpenException> RunPlainExpectingErrorAsync()
    {
        _security = new SecurityCapabilities(false);
        return await Assert.ThrowsAsync<DatabaseOpenException>(() => CreateUnlock().RunAsync());
    }

    [Fact]
    public async Task EncryptionOff_NoDatabase_OpensPlainWithoutWindowsOrStore()
    {
        _security = new SecurityCapabilities(false);

        var result = await CreateUnlock().RunAsync();

        Assert.NotNull(result);
        Assert.Null(result.Key);
        Assert.Null(result.DatabaseId);
        Assert.Empty(_prompts.ProtectRequests);
        Assert.Empty(_prompts.RecoveryRequests);
        Assert.Empty(_store.Keys);
        Assert.False(File.Exists(DbPath));
        _ = new SqliteDatabase(DbPath);
        Assert.Equal(DatabaseFileFormat.Plain, DatabaseFormatDetector.Detect(DbPath));
        Assert.Null(DatabaseKeyFile.Read(DbPath));
    }

    [Fact]
    public async Task EncryptionOff_PlainDatabase_IsOpenedAsIs()
    {
        CreatePlain(DbPath, "uk");
        var before = File.ReadAllBytes(DbPath);
        _security = new SecurityCapabilities(false);

        var result = await CreateUnlock().RunAsync();

        Assert.NotNull(result);
        Assert.Null(result.Key);
        Assert.Empty(_prompts.ProtectRequests);
        Assert.Empty(_prompts.RecoveryRequests);
        Assert.Equal(before, File.ReadAllBytes(DbPath));
        Assert.Equal(DatabaseFileFormat.Plain, DatabaseFormatDetector.Detect(DbPath));
        Assert.Null(DatabaseKeyFile.Read(DbPath));
    }

    [Fact]
    public async Task EncryptionOff_EncryptedDatabase_IsRefusedAndUntouched()
    {
        CreateEncrypted(DbPath);
        SqliteConnection.ClearAllPools();
        var dbBefore = File.ReadAllBytes(DbPath);
        var keyFileBefore = File.ReadAllBytes(DatabaseKeyFile.PathFor(DbPath));
        var files = Directory.GetFileSystemEntries(Path.GetDirectoryName(DbPath)!).Order().ToList();

        var ex = await RunPlainExpectingErrorAsync();

        Assert.Equal(DatabaseProblem.KeyRequired, ex.Problem);
        Assert.Empty(_prompts.ProtectRequests);
        Assert.Empty(_prompts.RecoveryRequests);
        Assert.Equal(dbBefore, File.ReadAllBytes(DbPath));
        Assert.Equal(keyFileBefore, File.ReadAllBytes(DatabaseKeyFile.PathFor(DbPath)));
        Assert.Equal(files, Directory.GetFileSystemEntries(Path.GetDirectoryName(DbPath)!).Order().ToList());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeKeyStore : IDatabaseKeyStore
    {
        public bool IsAvailable { get; set; } = true;

        public bool ThrowOnSave { get; set; }

        public Dictionary<Guid, DatabaseKey> Keys { get; } = [];

        public DatabaseKey? Get(Guid databaseId) => IsAvailable && Keys.TryGetValue(databaseId, out var key) ? key : null;

        public void Save(Guid databaseId, DatabaseKey key)
        {
            if (!IsAvailable || ThrowOnSave)
            {
                throw new DatabaseKeyStoreException("The store is not usable.");
            }

            Keys[databaseId] = key;
        }

        public void Delete(Guid databaseId) => Keys.Remove(databaseId);
    }

    private sealed class FakePrompts : IStartupPrompts
    {
        public Func<ProtectDataRequest, Task<ProtectDataOutcome>>? OnProtect { get; set; }

        public Func<RecoveryPromptRequest, Task<RecoveryPromptOutcome>>? OnRecovery { get; set; }

        public List<ProtectDataRequest> ProtectRequests { get; } = [];

        public List<RecoveryPromptRequest> RecoveryRequests { get; } = [];

        public Task<ProtectDataOutcome> ShowProtectDataAsync(ProtectDataRequest request)
        {
            ProtectRequests.Add(request);
            return (OnProtect ?? throw new InvalidOperationException("No protect window expected.")).Invoke(request);
        }

        public Task<RecoveryPromptOutcome> ShowRecoveryPromptAsync(RecoveryPromptRequest request)
        {
            RecoveryRequests.Add(request);
            return (OnRecovery ?? throw new InvalidOperationException("No recovery window expected.")).Invoke(request);
        }
    }

    private sealed class RecordingLanguage : IStartupLanguage
    {
        public List<string?> Applied { get; } = [];

        public void Apply(string? storedLanguage) => Applied.Add(storedLanguage);
    }
}
