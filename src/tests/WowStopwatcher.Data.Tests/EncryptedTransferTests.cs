using System.IO.Compression;
using Microsoft.Data.Sqlite;
using WowStopwatcher.Core.Security;

namespace WowStopwatcher.Data.Tests;

public sealed class EncryptedTransferTests : EncryptionTestBase
{
    private readonly DatabaseTransfer _transfer = new(TimeProvider.System);

    private string Sub(string name) => Path.Combine(Folder, name);

    private static string Db(string folder) => Path.Combine(folder, "wowstopwatcher.db");

    private static string KeyFileOf(string folder) => Path.Combine(folder, "wowstopwatcher.key.json");

    /// <summary>Creates an encrypted database with <paramref name="stopwatches"/> stopwatches and its key file in <paramref name="folder"/>.</summary>
    private static (DatabaseKey Key, DatabaseKeyFileContents KeyFile) CreateEncrypted(string folder, int stopwatches)
    {
        var key = DatabaseKey.Generate();
        var keyFile = NewKeyFile(key);
        var database = new SqliteDatabase(Db(folder), key);
        DatabaseKeyFile.Write(Db(folder), keyFile);
        using var connection = database.OpenConnection();
        for (var i = 0; i < stopwatches; i++)
        {
            Exec(connection, $"INSERT INTO stopwatches (id, name, color, sort_order) VALUES ('s{i}', 'S{i}', '#2F6FEB', {i})");
        }

        Exec(connection, "INSERT INTO sessions (stopwatch_id, start_utc, stop_utc) VALUES ('s0', 1000, 2000)");
        return (key, keyFile);
    }

    private static void CreatePlainIn(string folder, int stopwatches)
    {
        var database = new SqliteDatabase(Db(folder));
        using var connection = database.OpenConnection();
        for (var i = 0; i < stopwatches; i++)
        {
            Exec(connection, $"INSERT INTO stopwatches (id, name, color, sort_order) VALUES ('p{i}', 'P{i}', '#2F6FEB', {i})");
        }
    }

    private static long Stopwatches(string dbPath, DatabaseKey key)
    {
        using var connection = OpenRaw(dbPath, key);
        return Convert.ToInt64(Scalar(connection, "SELECT count(*) FROM stopwatches"));
    }

    private static void AssertSameKeyFile(string folderA, string folderB) =>
        Assert.Equal(File.ReadAllText(KeyFileOf(folderA)), File.ReadAllText(KeyFileOf(folderB)));

    private static void WriteTemplate(string folder, string name, string content)
    {
        var file = Path.Combine(folder, "templates", name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    private string MakeEncryptedBackup(string folder, DatabaseKey key, string password, string name = "b.wsbackup")
    {
        var path = Path.Combine(Folder, name);
        _transfer.ExportEncrypted(Db(folder), folder, path, key, password);
        return path;
    }

    // ---- copy ----

    [Fact]
    public void CopyVerified_Encrypted_CopiesDataAndKeyFile()
    {
        var (key, _) = CreateEncrypted(Sub("src"), 2);

        _transfer.CopyVerified(Db(Sub("src")), Db(Sub("dst")), key);

        Assert.Equal(DatabaseFileFormat.Encrypted, DatabaseFormatDetector.Detect(Db(Sub("dst"))));
        Assert.Equal(2, Stopwatches(Db(Sub("dst")), key));
        AssertSameKeyFile(Sub("src"), Sub("dst"));
        Assert.False(File.Exists(Db(Sub("dst")) + ".tmp"));
        Assert.Empty(Directory.GetFiles(Sub("dst"), "*.tmp"));
    }

    [Fact]
    public void CopyVerified_Encrypted_WhileSourceIsOpen_CopiesUncheckpointedData()
    {
        var (key, _) = CreateEncrypted(Sub("src"), 1);
        using var open = new SqliteDatabase(Db(Sub("src")), key).OpenConnection();
        Exec(open, "INSERT INTO stopwatches (id, name, color, sort_order) VALUES ('x', 'X', '#fff', 5)");

        _transfer.CopyVerified(Db(Sub("src")), Db(Sub("dst")), key);

        Assert.Equal(2, Stopwatches(Db(Sub("dst")), key));
    }

    [Fact]
    public void CopyVerified_Encrypted_ExistingTargetKeyFile_IsNeverOverwritten()
    {
        var (key, _) = CreateEncrypted(Sub("src"), 1);
        Directory.CreateDirectory(Sub("dst"));
        File.WriteAllText(KeyFileOf(Sub("dst")), "someone else's");

        var ex = Assert.Throws<DatabaseTransferException>(() => _transfer.CopyVerified(Db(Sub("src")), Db(Sub("dst")), key));

        Assert.Equal(TransferErrorCode.TargetExists, ex.Code);
        Assert.Equal("someone else's", File.ReadAllText(KeyFileOf(Sub("dst"))));
        Assert.False(File.Exists(Db(Sub("dst"))));
    }

    [Fact]
    public void CopyVerified_Encrypted_WrongKey_IsRejected_NothingLeft()
    {
        CreateEncrypted(Sub("src"), 1);

        var ex = Assert.Throws<DatabaseTransferException>(() => _transfer.CopyVerified(Db(Sub("src")), Db(Sub("dst")), DatabaseKey.Generate()));

        Assert.Equal(TransferErrorCode.KeyRejected, ex.Code);
        Assert.False(File.Exists(Db(Sub("dst"))));
        Assert.False(File.Exists(Db(Sub("dst")) + ".tmp"));
        Assert.False(File.Exists(KeyFileOf(Sub("dst"))));
    }

    [Fact]
    public void CopyVerified_Encrypted_WithoutKey_ByteCopiesClosedFileAndKeyFile()
    {
        var (key, _) = CreateEncrypted(Sub("src"), 3);
        SqliteConnection.ClearAllPools();

        _transfer.CopyVerified(Db(Sub("src")), Db(Sub("dst")));

        Assert.Equal(File.ReadAllBytes(Db(Sub("src"))), File.ReadAllBytes(Db(Sub("dst"))));
        AssertSameKeyFile(Sub("src"), Sub("dst"));
        Assert.Equal(3, Stopwatches(Db(Sub("dst")), key));
    }

    [Fact]
    public void CopyClosedVerified_RefusesDatabaseWithPendingWal_AndLeavesNothing()
    {
        var (key, _) = CreateEncrypted(Sub("src"), 1);
        using var open = new SqliteDatabase(Db(Sub("src")), key).OpenConnection();
        Exec(open, "INSERT INTO stopwatches (id, name, color, sort_order) VALUES ('x', 'X', '#fff', 5)");
        Assert.True(new FileInfo(Db(Sub("src")) + "-wal").Length > 0);

        var ex = Assert.Throws<DatabaseTransferException>(() => _transfer.CopyClosedVerified(Db(Sub("src")), Db(Sub("dst"))));

        Assert.Equal(TransferErrorCode.CopyFailed, ex.Code);
        Assert.False(File.Exists(Db(Sub("dst"))));
        Assert.False(File.Exists(Db(Sub("dst")) + ".tmp"));
        Assert.False(File.Exists(KeyFileOf(Sub("dst"))));
    }

    [Fact]
    public void BackupExisting_Encrypted_PutsKeyFileNextToBackup()
    {
        var (key, _) = CreateEncrypted(Sub("src"), 1);

        var backup = _transfer.BackupExisting(Db(Sub("src")), "import", key);

        Assert.Equal(1, Stopwatches(backup, key));
        Assert.Equal(File.ReadAllText(KeyFileOf(Sub("src"))), File.ReadAllText(DatabaseKeyFile.PathFor(backup)));
    }

    [Fact]
    public void MigrationBackup_Encrypted_GetsKeyFile()
    {
        var key = DatabaseKey.Generate();
        var keyFile = NewKeyFile(key);
        var original = new SqliteDatabase(DbPath, key);
        DatabaseKeyFile.Write(DbPath, keyFile);
        using (var connection = original.OpenConnection())
        {
            Exec(connection, "DROP TABLE goal_resets");
            Exec(connection, "PRAGMA user_version = 13");
        }

        var migrated = new SqliteDatabase(DbPath, key);

        Assert.NotNull(migrated.BackupPath);
        Assert.Equal(File.ReadAllText(DatabaseKeyFile.PathFor(DbPath)), File.ReadAllText(DatabaseKeyFile.PathFor(migrated.BackupPath!)));
        Assert.Equal(Path.Combine(BackupsFolder, "wowstopwatcher-v13.key.json"), DatabaseKeyFile.PathFor(migrated.BackupPath!));
    }

    [Fact]
    public void BackupCleaner_AlsoRemovesTheKeyFileOfAnOldMigrationBackup()
    {
        var key = DatabaseKey.Generate();
        DatabaseKeyFile.Write(DbPath, NewKeyFile(key));
        var database = new SqliteDatabase(DbPath, key);
        using (var connection = database.OpenConnection())
        {
            Exec(connection, "DROP TABLE goal_resets");
            Exec(connection, "PRAGMA user_version = 13");
        }

        var backup = new SqliteDatabase(DbPath, key).BackupPath!;
        SqliteConnection.ClearAllPools();
        var later = new FutureTimeProvider(DateTimeOffset.UtcNow.AddDays(30));

        new DatabaseBackupCleaner(DbPath, later).DeleteOlderThan(TimeSpan.FromDays(7));

        Assert.False(File.Exists(backup));
        Assert.False(File.Exists(DatabaseKeyFile.PathFor(backup)));
    }

    private sealed class FutureTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // ---- inspect ----

    [Fact]
    public void Inspect_Encrypted_RightNoAndWrongKey()
    {
        var (key, _) = CreateEncrypted(Sub("a"), 2);
        SqliteConnection.ClearAllPools();

        var ok = _transfer.Inspect(Db(Sub("a")), key);
        var none = _transfer.Inspect(Db(Sub("a")));
        var wrong = _transfer.Inspect(Db(Sub("a")), DatabaseKey.Generate());

        Assert.True(ok.IntegrityOk);
        Assert.Null(ok.KeyProblem);
        Assert.Equal(2, ok.StopwatchCount);
        Assert.False(none.IntegrityOk);
        Assert.Equal(TransferErrorCode.KeyRequired, none.KeyProblem);
        Assert.False(wrong.IntegrityOk);
        Assert.Equal(TransferErrorCode.KeyRejected, wrong.KeyProblem);
    }

    [Fact]
    public void Inspect_Plain_IgnoresKey()
    {
        CreatePlainIn(Sub("p"), 2);

        var info = _transfer.Inspect(Db(Sub("p")), DatabaseKey.Generate());

        Assert.True(info.IntegrityOk);
        Assert.Null(info.KeyProblem);
        Assert.Equal(2, info.StopwatchCount);
    }

    // ---- export / import ----

    [Fact]
    public void ExportEncrypted_RoundTrip_ReturnsKeyKeyFileDatabaseAndTemplates()
    {
        var (key, keyFile) = CreateEncrypted(Sub("src"), 2);
        WriteTemplate(Sub("src"), "t.docx", "tmpl");
        var backup = MakeEncryptedBackup(Sub("src"), key, "correct horse");

        Assert.Equal(new byte[] { (byte)'W', (byte)'S', (byte)'B', (byte)'K' }, File.ReadAllBytes(backup).Take(4).ToArray());
        Assert.False(File.Exists(backup + ".tmp"));

        using var validation = _transfer.ValidateImport(backup, "correct horse");

        Assert.True(validation.IsValid, validation.Error?.ToString());
        Assert.True(key.KeyEquals(validation.Key));
        Assert.Equal(keyFile.DatabaseId, validation.KeyFile!.DatabaseId);
        Assert.Equal(2, validation.Inspection!.StopwatchCount);
        Assert.Equal(2, Stopwatches(validation.DatabasePath!, key));
        Assert.Equal("tmpl", File.ReadAllText(Path.Combine(validation.TemplatesFolder!, "t.docx")));
        Assert.True(File.Exists(DatabaseKeyFile.PathFor(validation.DatabasePath!)));
    }

    [Fact]
    public void ExportEncrypted_FileIsNotAZipAndHoldsNoPlainSqliteHeader()
    {
        var (key, _) = CreateEncrypted(Sub("src"), 1);
        var backup = MakeEncryptedBackup(Sub("src"), key, "pw-password-1");

        var bytes = File.ReadAllBytes(backup);

        Assert.False(bytes.AsSpan().IndexOf("SQLite format 3"u8) >= 0);
        Assert.False(bytes.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(key.ToHex())) >= 0);
        Assert.ThrowsAny<InvalidDataException>(() => ZipFile.OpenRead(backup));
    }

    [Fact]
    public void ValidateImport_Envelope_WrongOrMissingPassword()
    {
        var (key, _) = CreateEncrypted(Sub("src"), 1);
        var backup = MakeEncryptedBackup(Sub("src"), key, "right-password");

        using var wrong = _transfer.ValidateImport(backup, "wrong");
        using var none = _transfer.ValidateImport(backup);

        Assert.Equal(TransferErrorCode.WrongPassword, wrong.Error);
        Assert.Equal(TransferErrorCode.PasswordRequired, none.Error);
        Assert.Null(wrong.DatabasePath);
    }

    [Fact]
    public void ValidateImport_Envelope_Tampered_IsNotValid()
    {
        var (key, _) = CreateEncrypted(Sub("src"), 1);
        var backup = MakeEncryptedBackup(Sub("src"), key, "pw-password-1");
        var bytes = File.ReadAllBytes(backup);
        bytes[bytes.Length / 2] ^= 0x01;
        File.WriteAllBytes(backup, bytes);

        using var validation = _transfer.ValidateImport(backup, "pw-password-1");

        Assert.False(validation.IsValid);
        Assert.Equal(TransferErrorCode.WrongPassword, validation.Error);
    }

    [Fact]
    public void ValidateImport_Envelope_Truncated_IsDamaged()
    {
        var (key, _) = CreateEncrypted(Sub("src"), 1);
        var backup = MakeEncryptedBackup(Sub("src"), key, "pw-password-1");
        File.WriteAllBytes(backup, File.ReadAllBytes(backup).Take(20).ToArray());

        using var validation = _transfer.ValidateImport(backup, "pw-password-1");

        Assert.Equal(TransferErrorCode.Damaged, validation.Error);
    }

    [Fact]
    public void ExportEncrypted_PasswordShorterThanTheMinimum_Throws_AndWritesNothing()
    {
        var (key, _) = CreateEncrypted(Sub("src"), 1);
        var path = Path.Combine(Folder, "short.wsbackup");

        Assert.Throws<ArgumentException>(() =>
            _transfer.ExportEncrypted(Db(Sub("src")), Sub("src"), path, key, new string('a', BackupEnvelope.MinPasswordLength - 1)));

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void ExportEncrypted_PasswordOfExactlyTheMinimum_IsAccepted()
    {
        var (key, _) = CreateEncrypted(Sub("src"), 1);
        var path = Path.Combine(Folder, "min.wsbackup");

        _transfer.ExportEncrypted(Db(Sub("src")), Sub("src"), path, key, new string('a', BackupEnvelope.MinPasswordLength));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void ImportOfABackupWithAShortPassword_StillWorks()
    {
        var path = Path.Combine(Folder, "old.wsbackup");
        File.WriteAllBytes(path, BackupEnvelope.Encrypt(new byte[] { 1, 2, 3 }, "pw", 1000));

        using var validation = _transfer.ValidateImport(path, "pw");

        // The short password opens the envelope; the payload is not a backup zip, so it is reported as damaged, not as a wrong password.
        Assert.Equal(TransferErrorCode.Damaged, validation.Error);
    }

    [Fact]
    public void ExportEncrypted_EmptyPassword_Throws_AndWritesNothing()
    {
        var (key, _) = CreateEncrypted(Sub("src"), 1);
        var path = Path.Combine(Folder, "x.wsbackup");

        Assert.Throws<ArgumentException>(() => _transfer.ExportEncrypted(Db(Sub("src")), Sub("src"), path, key, ""));

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void ExportEncrypted_PlainDatabase_OrMissingKeyFile_IsRefused()
    {
        CreatePlainIn(Sub("p"), 1);
        var (key, _) = CreateEncrypted(Sub("e"), 1);
        File.Delete(KeyFileOf(Sub("e")));
        var path = Path.Combine(Folder, "x.wsbackup");

        var plain = Assert.Throws<DatabaseTransferException>(() => _transfer.ExportEncrypted(Db(Sub("p")), Sub("p"), path, key, "pw-password-1"));
        var noKeyFile = Assert.Throws<DatabaseTransferException>(() => _transfer.ExportEncrypted(Db(Sub("e")), Sub("e"), path, key, "pw-password-1"));

        Assert.Equal(TransferErrorCode.CopyFailed, plain.Code);
        Assert.Equal(TransferErrorCode.KeyFileMissing, noKeyFile.Code);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Export_PlainZip_OfEncryptedDatabase_IsRefused_AndOldPlainZipStillImports()
    {
        CreateEncrypted(Sub("e"), 1);
        SqliteConnection.ClearAllPools();
        var zip = Path.Combine(Folder, "old.zip");

        var ex = Assert.Throws<DatabaseTransferException>(() => _transfer.Export(Db(Sub("e")), Sub("e"), zip));
        Assert.Equal(TransferErrorCode.KeyRequired, ex.Code);
        Assert.False(File.Exists(zip));

        CreatePlainIn(Sub("p"), 2);
        WriteTemplate(Sub("p"), "t.docx", "tmpl");
        _transfer.Export(Db(Sub("p")), Sub("p"), zip);

        using var validation = _transfer.ValidateImport(zip);
        Assert.True(validation.IsValid);
        Assert.Null(validation.Key);
        Assert.Null(validation.KeyFile);
        Assert.Equal(2, validation.Inspection!.StopwatchCount);
        Assert.Equal(DatabaseFileFormat.Plain, DatabaseFormatDetector.Detect(validation.DatabasePath!));
    }

    [Fact]
    public void ValidateImport_PlainZipWithEncryptedFile_IsDamaged()
    {
        CreateEncrypted(Sub("e"), 1);
        SqliteConnection.ClearAllPools();
        var zip = Path.Combine(Folder, "hand-made.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(Db(Sub("e")), "wowstopwatcher.db");
        }

        using var validation = _transfer.ValidateImport(zip);

        Assert.Equal(TransferErrorCode.Damaged, validation.Error);
    }

    // ---- pending steps ----

    private DatabaseLocation Location() => new(Sub("default"));

    [Fact]
    public void ApplyPending_Move_Encrypted_WithKey_CopiesDataAndKeyFile_AndSwitches()
    {
        var (key, _) = CreateEncrypted(Sub("src"), 2);
        var location = Location();
        location.SetPending(new PendingStep(PendingKind.Move, Db(Sub("src")), Db(Sub("dst"))));

        var outcome = _transfer.ApplyPending(location, key);

        Assert.True(outcome!.Success, outcome.Message);
        Assert.Equal(2, Stopwatches(Db(Sub("dst")), key));
        AssertSameKeyFile(Sub("src"), Sub("dst"));
        Assert.Equal(Sub("dst"), location.Read().Settings.DatabaseFolder);
        Assert.True(File.Exists(Db(Sub("src"))));
    }

    [Fact]
    public void ApplyPending_Move_Encrypted_WithoutKey_UsesByteCopy()
    {
        var (key, _) = CreateEncrypted(Sub("src"), 2);
        SqliteConnection.ClearAllPools();
        var location = Location();
        location.SetPending(new PendingStep(PendingKind.Move, Db(Sub("src")), Db(Sub("dst"))));

        var outcome = _transfer.ApplyPending(location);

        Assert.True(outcome!.Success, outcome.Message);
        Assert.Equal(File.ReadAllBytes(Db(Sub("src"))), File.ReadAllBytes(Db(Sub("dst"))));
        AssertSameKeyFile(Sub("src"), Sub("dst"));
        Assert.Equal(2, Stopwatches(Db(Sub("dst")), key));
    }

    [Fact]
    public void ApplyPending_Replace_Encrypted_BacksUpOtherDatabaseWithItsKeyFile_ByByteCopy()
    {
        var (mine, _) = CreateEncrypted(Sub("src"), 3);
        var (theirs, _) = CreateEncrypted(Sub("dst"), 1);
        SqliteConnection.ClearAllPools();
        var theirKeyFile = File.ReadAllText(KeyFileOf(Sub("dst")));
        var location = Location();
        location.SetPending(new PendingStep(PendingKind.Replace, Db(Sub("src")), Db(Sub("dst"))));

        var outcome = _transfer.ApplyPending(location, mine);

        Assert.True(outcome!.Success, outcome.Message);
        Assert.Equal(3, Stopwatches(Db(Sub("dst")), mine));
        AssertSameKeyFile(Sub("src"), Sub("dst"));

        var backup = Directory.GetFiles(Path.Combine(Sub("dst"), "backups"), "wowstopwatcher-before-replace-*.db").Single();
        Assert.Equal(1, Stopwatches(backup, theirs));
        Assert.Equal(theirKeyFile, File.ReadAllText(DatabaseKeyFile.PathFor(backup)));

        var aside = Directory.GetFiles(Sub("dst"), "wowstopwatcher.key.json.replaced-*").Single();
        Assert.Equal(theirKeyFile, File.ReadAllText(aside));
        Assert.Single(Directory.GetFiles(Sub("dst"), "wowstopwatcher.db.replaced-*"));
        Assert.Empty(Directory.GetFiles(Sub("dst"), "*.incoming"));
    }

    /// <summary>Validates a backup made from <c>src</c> and stages it in the current data folder (the default folder of <see cref="Location"/>).</summary>
    private string Stage(DatabaseKey sourceKey, string password, out DatabaseKey importedKey)
    {
        var backupFile = MakeEncryptedBackup(Sub("src"), sourceKey, password);
        using var validation = _transfer.ValidateImport(backupFile, password);
        Assert.True(validation.IsValid, validation.Error?.ToString());
        importedKey = validation.Key!;
        return _transfer.StageImport(validation, Sub("default"));
    }

    private string StagingRoot => Path.Combine(Sub("default"), "import-staging");

    [Fact]
    public void StageImport_WritesDatabaseKeyFileAndTemplates_ButNeverTheRawKey()
    {
        var (key, keyFile) = CreateEncrypted(Sub("src"), 2);
        WriteTemplate(Sub("src"), "t.docx", "tmpl");

        var staged = Stage(key, "pw-password-1", out var imported);

        Assert.Equal(StagingRoot, Path.GetDirectoryName(staged));
        Assert.True(key.KeyEquals(imported));
        Assert.Equal(2, Stopwatches(Db(staged), key));
        Assert.Equal(keyFile.DatabaseId, DatabaseKeyFile.Read(Db(staged))!.DatabaseId);
        Assert.Equal("tmpl", File.ReadAllText(Path.Combine(staged, "templates", "t.docx")));
        var hex = System.Text.Encoding.ASCII.GetBytes(key.ToHex());
        foreach (var file in Directory.EnumerateFiles(StagingRoot, "*", SearchOption.AllDirectories))
        {
            Assert.False(File.ReadAllBytes(file).AsSpan().IndexOf(hex) >= 0, file);
            Assert.NotEqual("database.key", Path.GetFileName(file));
        }
    }

    [Fact]
    public void StageImport_RejectsInvalidAndPlainZipValidations()
    {
        var (key, _) = CreateEncrypted(Sub("src"), 1);
        var backupFile = MakeEncryptedBackup(Sub("src"), key, "pw-password-1");
        using var wrong = _transfer.ValidateImport(backupFile, "wrong");
        CreatePlainIn(Sub("p"), 1);
        var zip = Path.Combine(Folder, "old.zip");
        _transfer.Export(Db(Sub("p")), Sub("p"), zip);
        using var plainZip = _transfer.ValidateImport(zip);

        Assert.Throws<ArgumentException>(() => _transfer.StageImport(wrong, Sub("default")));
        Assert.Throws<ArgumentException>(() => _transfer.StageImport(plainZip, Sub("default")));
        Assert.False(Directory.Exists(StagingRoot));
    }

    [Fact]
    public void ApplyPending_Import_Staged_NeedsNoPassword_PlacesDatabaseAndKeyFile_AndDeletesStaging()
    {
        var (sourceKey, sourceKeyFile) = CreateEncrypted(Sub("src"), 3);
        var (currentKey, _) = CreateEncrypted(Sub("default"), 1);
        SqliteConnection.ClearAllPools();
        var currentKeyFile = File.ReadAllText(KeyFileOf(Sub("default")));
        var staged = Stage(sourceKey, "pw-password-1", out _);
        var location = Location();
        location.SetPending(new PendingStep(PendingKind.Import, staged, Db(Sub("default"))));

        var outcome = _transfer.ApplyPending(location, currentKey);

        Assert.True(outcome!.Success, outcome.Message);
        Assert.Equal(3, Stopwatches(Db(Sub("default")), sourceKey));
        Assert.Equal(sourceKeyFile.DatabaseId, DatabaseKeyFile.Read(Db(Sub("default")))!.DatabaseId);
        var backup = Directory.GetFiles(Path.Combine(Sub("default"), "backups"), "wowstopwatcher-before-import-*.db").Single();
        Assert.Equal(1, Stopwatches(backup, currentKey));
        Assert.Equal(currentKeyFile, File.ReadAllText(DatabaseKeyFile.PathFor(backup)));
        Assert.Single(Directory.GetFiles(Sub("default"), "wowstopwatcher.key.json.replaced-*"));
        Assert.False(Directory.Exists(staged));
        Assert.False(Directory.Exists(StagingRoot));
        Assert.Null(location.Read().Settings.Pending);
    }

    [Fact]
    public void ApplyPending_Import_Staged_Failure_ChangesNothing_AndDeletesStaging()
    {
        var (sourceKey, _) = CreateEncrypted(Sub("src"), 3);
        CreateEncrypted(Sub("default"), 1);
        SqliteConnection.ClearAllPools();
        var dbBytes = File.ReadAllBytes(Db(Sub("default")));
        var keyFileText = File.ReadAllText(KeyFileOf(Sub("default")));
        var staged = Stage(sourceKey, "pw-password-1", out _);
        File.Delete(Db(staged));
        var location = Location();
        location.SetPending(new PendingStep(PendingKind.Import, staged, Db(Sub("default"))));

        var outcome = _transfer.ApplyPending(location);

        Assert.Equal(TransferErrorCode.NoDatabase, outcome!.Error);
        Assert.Equal(dbBytes, File.ReadAllBytes(Db(Sub("default"))));
        Assert.Equal(keyFileText, File.ReadAllText(KeyFileOf(Sub("default"))));
        Assert.False(Directory.Exists(staged));
        Assert.False(Directory.Exists(Path.Combine(Sub("default"), "backups")));
    }

    [Fact]
    public void ApplyPending_LeftoverStagingWithoutPendingStep_IsRemoved()
    {
        var (sourceKey, _) = CreateEncrypted(Sub("src"), 1);
        var staged = Stage(sourceKey, "pw-password-1", out _);
        Assert.True(Directory.Exists(staged));

        var outcome = _transfer.ApplyPending(Location());

        Assert.Null(outcome);
        Assert.False(Directory.Exists(StagingRoot));
    }

    [Fact]
    public void ApplyPending_OtherStagingFolders_AreRemoved_WhileTheReferencedOneIsApplied()
    {
        var (sourceKey, _) = CreateEncrypted(Sub("src"), 3);
        var (currentKey, _) = CreateEncrypted(Sub("default"), 1);
        SqliteConnection.ClearAllPools();
        var stale = Stage(sourceKey, "pw-password-1", out _);
        var staged = Path.Combine(StagingRoot, "zz-current");
        Directory.Move(Stage(sourceKey, "pw-password-1", out _), staged);
        var location = Location();
        location.SetPending(new PendingStep(PendingKind.Import, staged, Db(Sub("default"))));

        var outcome = _transfer.ApplyPending(location, currentKey);

        Assert.True(outcome!.Success, outcome.Message);
        Assert.False(Directory.Exists(stale));
        Assert.False(Directory.Exists(StagingRoot));
        Assert.Equal(3, Stopwatches(Db(Sub("default")), sourceKey));
    }

    [Fact]
    public void DiscardStaging_DeletesOnlyFoldersInsideImportStaging()
    {
        var (sourceKey, _) = CreateEncrypted(Sub("src"), 1);
        var staged = Stage(sourceKey, "pw-password-1", out _);

        _transfer.DiscardStaging(Sub("src"));
        Assert.True(File.Exists(Db(Sub("src"))));

        _transfer.DiscardStaging(staged);
        Assert.False(Directory.Exists(staged));
        Assert.False(Directory.Exists(StagingRoot));
    }

    [Fact]
    public void ApplyPending_Import_EnvelopeDirectly_NeedsStaging_AndChangesNothing()
    {
        var (sourceKey, _) = CreateEncrypted(Sub("src"), 3);
        var backupFile = MakeEncryptedBackup(Sub("src"), sourceKey, "pw-password-1");
        var (currentKey, _) = CreateEncrypted(Sub("default"), 1);
        SqliteConnection.ClearAllPools();
        var dbBytes = File.ReadAllBytes(Db(Sub("default")));
        var location = Location();
        location.SetPending(new PendingStep(PendingKind.Import, backupFile, Db(Sub("default"))));

        var outcome = _transfer.ApplyPending(location, currentKey);

        Assert.Equal(TransferErrorCode.PasswordRequired, outcome!.Error);
        Assert.Equal(dbBytes, File.ReadAllBytes(Db(Sub("default"))));
        Assert.True(File.Exists(backupFile));
    }

    [Fact]
    public void ApplyPending_Import_PlainZip_ReplacesEncryptedCurrent_AndSetsItsKeyFileAside()
    {
        CreatePlainIn(Sub("old"), 2);
        var zip = Path.Combine(Folder, "old.zip");
        _transfer.Export(Db(Sub("old")), Sub("old"), zip);
        var (currentKey, _) = CreateEncrypted(Sub("cur"), 1);
        SqliteConnection.ClearAllPools();
        var location = Location();
        location.SetPending(new PendingStep(PendingKind.Import, zip, Db(Sub("cur"))));

        var outcome = _transfer.ApplyPending(location, currentKey);

        Assert.True(outcome!.Success, outcome.Message);
        Assert.Equal(DatabaseFileFormat.Plain, DatabaseFormatDetector.Detect(Db(Sub("cur"))));
        Assert.Equal(2, _transfer.Inspect(Db(Sub("cur"))).StopwatchCount);
        Assert.False(File.Exists(KeyFileOf(Sub("cur"))));
        Assert.Single(Directory.GetFiles(Sub("cur"), "wowstopwatcher.key.json.replaced-*"));
        var backup = Directory.GetFiles(Path.Combine(Sub("cur"), "backups"), "wowstopwatcher-before-import-*.db").Single();
        Assert.Equal(1, Stopwatches(backup, currentKey));
    }
}
