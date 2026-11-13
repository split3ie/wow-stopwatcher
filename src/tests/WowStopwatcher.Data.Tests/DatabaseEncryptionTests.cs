using WowStopwatcher.Core.Security;

namespace WowStopwatcher.Data.Tests;

public sealed class DatabaseEncryptionTests : EncryptionTestBase
{
    private string AsidePath => DbPath + DatabaseEncryption.AsideSuffix;

    private string TempPath => DbPath + DatabaseEncryption.TempSuffix;

    private string KeyPath => DatabaseKeyFile.PathFor(DbPath);

    private void AssertPlainOriginalIntact()
    {
        Assert.True(HasPlainHeader(DbPath));
        Assert.Equal(2, CountSessions(new SqliteDatabase(DbPath)));
        Assert.False(File.Exists(TempPath));
        Assert.False(File.Exists(AsidePath));
    }

    private void AssertEncryptedIntact(DatabaseKey key)
    {
        Assert.Equal(DatabaseFileFormat.Encrypted, DatabaseFormatDetector.Detect(DbPath));
        Assert.Equal(2, CountSessions(new SqliteDatabase(DbPath, key)));
        Assert.False(File.Exists(TempPath));
        Assert.False(File.Exists(AsidePath));
    }

    /// <summary>Builds, in a sub folder, the plain original (copied before) and the encrypted result of encrypting it.</summary>
    private (string PlainCopy, string Encrypted, string KeyFile, DatabaseKey Key) BuildPair()
    {
        var sub = Path.Combine(Folder, "pair");
        Directory.CreateDirectory(sub);
        var db = Path.Combine(sub, "wowstopwatcher.db");
        CreatePlain(db);
        var plainCopy = Path.Combine(sub, "plain-copy.bin");
        File.Copy(db, plainCopy);
        var key = DatabaseKey.Generate();
        new DatabaseEncryption(db).Encrypt(key, NewKeyFile(key));
        return (plainCopy, db, DatabaseKeyFile.PathFor(db), key);
    }

    [Fact]
    public void Encrypt_EncryptsInPlace_KeepsDataAndRemovesPlainLeftovers()
    {
        CreatePlain();
        Directory.CreateDirectory(BackupsFolder);
        var plainBackup1 = Path.Combine(BackupsFolder, "wowstopwatcher-v6.db");
        var plainBackup2 = Path.Combine(BackupsFolder, "wowstopwatcher-v6-2.db");
        var encryptedBackup = Path.Combine(BackupsFolder, "wowstopwatcher-v5.db");
        var otherDb = Path.Combine(BackupsFolder, "other-v6.db");
        var note = Path.Combine(BackupsFolder, "wowstopwatcher-v6.txt");
        CreatePlain(plainBackup1);
        File.Copy(plainBackup1, plainBackup2);
        CreatePlain(otherDb);
        File.WriteAllText(note, "keep");
        var key = DatabaseKey.Generate();
        _ = new SqliteDatabase(encryptedBackup, key);
        var keyFile = NewKeyFile(key);

        new DatabaseEncryption(DbPath).Encrypt(key, keyFile);

        AssertEncryptedIntact(key);
        Assert.False(File.Exists(DbPath + "-wal"));
        Assert.False(File.Exists(plainBackup1));
        Assert.False(File.Exists(plainBackup2));
        Assert.True(File.Exists(encryptedBackup));
        Assert.True(File.Exists(otherDb));
        Assert.True(File.Exists(note));
        Assert.Equal(["other-v6.db", "wowstopwatcher-v5.db", "wowstopwatcher-v6.txt"], Directory.GetFiles(BackupsFolder).Select(f => Path.GetFileName(f)!).Order().ToArray());
        Assert.Equal(keyFile.DatabaseId, DatabaseKeyFile.Read(DbPath)!.DatabaseId);
        Assert.Equal(DatabaseProblem.KeyRequired, Assert.Throws<DatabaseOpenException>(() => new SqliteDatabase(DbPath)).Problem);
        Assert.Equal(DatabaseProblem.KeyRejected, Assert.Throws<DatabaseOpenException>(() => new SqliteDatabase(DbPath, DatabaseKey.Generate())).Problem);
    }

    [Fact]
    public void Encrypt_OfDatabaseWithPendingWal_KeepsAllRows()
    {
        CreatePlain();
        var crashDb = Path.Combine(Folder, "crash.db");
        using (var holder = new SqliteDatabase(DbPath).OpenConnection())
        {
            Exec(holder, "INSERT INTO sessions (stopwatch_id, start_utc, stop_utc) VALUES ('a', 5000, 6000)");

            // Copy the files while the connection is open: the image a crash would leave, rows still only in the -wal file.
            File.Copy(DbPath, crashDb);
            File.Copy(DbPath + "-wal", crashDb + "-wal");
        }

        var key = DatabaseKey.Generate();
        new DatabaseEncryption(crashDb).Encrypt(key, NewKeyFile(key));

        Assert.Equal(3, CountSessions(new SqliteDatabase(crashDb, key)));
    }

    [Fact]
    public void Encrypt_OnEncryptedOrMissingDatabase_Throws()
    {
        var key = DatabaseKey.Generate();
        Assert.Throws<DatabaseEncryptionException>(() => new DatabaseEncryption(DbPath).Encrypt(key, NewKeyFile(key)));

        _ = new SqliteDatabase(DbPath, key);
        Assert.Throws<DatabaseEncryptionException>(() => new DatabaseEncryption(DbPath).Encrypt(key, NewKeyFile(key)));
    }

    [Fact]
    public void Encrypt_WhenAStepFails_LeavesThePlainOriginalUsable()
    {
        CreatePlain();
        File.WriteAllText(BackupsFolder, "a file where the backups folder should be");
        var key = DatabaseKey.Generate();

        Assert.Throws<DatabaseEncryptionException>(() => new DatabaseEncryption(DbPath).Encrypt(key, NewKeyFile(key)));

        AssertPlainOriginalIntact();
        Assert.False(File.Exists(KeyPath));
        Assert.Equal("a file where the backups folder should be", File.ReadAllText(BackupsFolder));
    }

    [Fact]
    public void Recover_WithNothingLeftBehind_DoesNothing()
    {
        CreatePlain();

        Assert.Equal(EncryptionRecoveryResult.NothingToDo, new DatabaseEncryption(DbPath).Recover(null));
        Assert.Equal(EncryptionRecoveryResult.NothingToDo, new DatabaseEncryption(Path.Combine(Folder, "none.db")).Recover(null));
        AssertPlainOriginalIntact();
    }

    [Fact]
    public void Recover_PlainWithStaleTempAndBackup_RollsBack()
    {
        CreatePlain();
        File.WriteAllText(TempPath, "half written");
        Directory.CreateDirectory(BackupsFolder);
        var backup = Path.Combine(BackupsFolder, "wowstopwatcher-before-encryption.db");
        File.Copy(DbPath, backup);
        var otherBackup = Path.Combine(BackupsFolder, "wowstopwatcher-v6.db");
        File.Copy(DbPath, otherBackup);
        DatabaseKeyFile.Write(DbPath, NewKeyFile(DatabaseKey.Generate()));

        var result = new DatabaseEncryption(DbPath).Recover(DatabaseKey.Generate());

        Assert.Equal(EncryptionRecoveryResult.RolledBack, result);
        AssertPlainOriginalIntact();
        Assert.False(File.Exists(backup));
        Assert.False(File.Exists(KeyPath));
        Assert.True(File.Exists(otherBackup));
    }

    [Fact]
    public void Recover_CrashBetweenMoves_WithVerifiedTemp_FinishesTheSwap()
    {
        var (plainCopy, encrypted, keyFile, key) = BuildPair();
        File.Move(plainCopy, AsidePath);
        File.Move(encrypted, TempPath);
        File.Move(keyFile, KeyPath);
        Assert.False(File.Exists(DbPath));

        var result = new DatabaseEncryption(DbPath).Recover(key);

        Assert.Equal(EncryptionRecoveryResult.Completed, result);
        AssertEncryptedIntact(key);
    }

    [Fact]
    public void Recover_CrashBetweenMoves_WithDamagedTemp_RestoresThePlainOriginal()
    {
        var (plainCopy, encrypted, keyFile, key) = BuildPair();
        File.Move(plainCopy, AsidePath);
        File.WriteAllBytes(TempPath, File.ReadAllBytes(encrypted)[..5000]);
        File.Move(keyFile, KeyPath);

        var result = new DatabaseEncryption(DbPath).Recover(key);

        Assert.Equal(EncryptionRecoveryResult.RolledBack, result);
        AssertPlainOriginalIntact();
    }

    [Fact]
    public void Recover_CrashBetweenMoves_WithoutKey_RestoresThePlainOriginal()
    {
        var (plainCopy, encrypted, keyFile, _) = BuildPair();
        File.Move(plainCopy, AsidePath);
        File.Move(encrypted, TempPath);
        File.Move(keyFile, KeyPath);

        var result = new DatabaseEncryption(DbPath).Recover(null);

        Assert.Equal(EncryptionRecoveryResult.RolledBack, result);
        AssertPlainOriginalIntact();
    }

    [Fact]
    public void Recover_CrashBetweenMovesWithoutTemp_RestoresThePlainOriginal()
    {
        var (plainCopy, _, _, key) = BuildPair();
        File.Move(plainCopy, AsidePath);

        Assert.Equal(EncryptionRecoveryResult.RolledBack, new DatabaseEncryption(DbPath).Recover(key));
        AssertPlainOriginalIntact();
    }

    [Fact]
    public void Recover_EncryptedWithLeftoverPlainFiles_DeletesThemAfterVerifyingTheKey()
    {
        var (plainCopy, encrypted, keyFile, key) = BuildPair();
        File.Move(encrypted, DbPath);
        File.Move(keyFile, KeyPath);
        File.Move(plainCopy, AsidePath);
        Directory.CreateDirectory(BackupsFolder);
        var schemaBackup = Path.Combine(BackupsFolder, "wowstopwatcher-v6.db");
        File.Copy(AsidePath, schemaBackup);

        var result = new DatabaseEncryption(DbPath).Recover(key);

        Assert.Equal(EncryptionRecoveryResult.Completed, result);
        AssertEncryptedIntact(key);
        Assert.False(File.Exists(schemaBackup));
    }

    [Fact]
    public void Recover_EncryptedWithLeftoverPlainFiles_WithWrongKey_KeepsEverything()
    {
        var (plainCopy, encrypted, keyFile, _) = BuildPair();
        File.Move(encrypted, DbPath);
        File.Move(keyFile, KeyPath);
        File.Move(plainCopy, AsidePath);

        var result = new DatabaseEncryption(DbPath).Recover(DatabaseKey.Generate());

        Assert.Equal(EncryptionRecoveryResult.Conflict, result);
        Assert.True(File.Exists(AsidePath));
        Assert.True(File.Exists(DbPath));
    }

    [Fact]
    public void Recover_PlainDatabaseWithAsideFile_ReportsConflictAndDeletesNothing()
    {
        CreatePlain();
        File.Copy(DbPath, AsidePath);

        Assert.Equal(EncryptionRecoveryResult.Conflict, new DatabaseEncryption(DbPath).Recover(null));
        Assert.True(File.Exists(AsidePath));
        Assert.True(File.Exists(DbPath));
    }
}
