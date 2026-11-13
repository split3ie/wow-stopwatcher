using WowStopwatcher.Core.Security;

namespace WowStopwatcher.Data.Tests;

public sealed class EncryptedDatabaseTests : EncryptionTestBase
{
    [Fact]
    public void Detect_ReportsMissingEmptyPlainAndEncrypted()
    {
        Assert.Equal(DatabaseFileFormat.Missing, DatabaseFormatDetector.Detect(DbPath));

        var empty = Path.Combine(Folder, "empty.db");
        File.WriteAllBytes(empty, []);
        Assert.Equal(DatabaseFileFormat.Missing, DatabaseFormatDetector.Detect(empty));

        var plain = Path.Combine(Folder, "plain.db");
        CreatePlain(plain);
        Assert.Equal(DatabaseFileFormat.Plain, DatabaseFormatDetector.Detect(plain));

        _ = new SqliteDatabase(DbPath, DatabaseKey.Generate());
        Assert.Equal(DatabaseFileFormat.Encrypted, DatabaseFormatDetector.Detect(DbPath));
    }

    [Fact]
    public void KeyConstructor_CreatesEncryptedDatabaseAndReopensIt()
    {
        var key = DatabaseKey.Generate();
        var created = new SqliteDatabase(DbPath, key);
        Assert.True(created.IsEncrypted);
        using (var connection = created.OpenConnection())
        {
            Exec(connection, "INSERT INTO stopwatches (id, name, color, sort_order) VALUES ('a', 'English', '#2F6FEB', 0)");
        }

        var reopened = new SqliteDatabase(DbPath, key);

        Assert.Equal(SqliteDatabase.LatestSchemaVersion, reopened.GetSchemaVersion());
        using var read = reopened.OpenConnection();
        Assert.Equal("English", Scalar(read, "SELECT name FROM stopwatches"));
        Assert.Equal(1L, Scalar(read, "PRAGMA foreign_keys"));
        Assert.Equal("wal", Scalar(read, "PRAGMA journal_mode"));
        Assert.Equal(DatabaseFileFormat.Encrypted, DatabaseFormatDetector.Detect(DbPath));
    }

    [Fact]
    public void KeyConstructor_WrongKey_ThrowsKeyRejected()
    {
        _ = new SqliteDatabase(DbPath, DatabaseKey.Generate());

        var ex = Assert.Throws<DatabaseOpenException>(() => new SqliteDatabase(DbPath, DatabaseKey.Generate()));

        Assert.Equal(DatabaseProblem.KeyRejected, ex.Problem);
    }

    [Fact]
    public void PathOnlyConstructor_OnEncryptedFile_ThrowsKeyRequired()
    {
        _ = new SqliteDatabase(DbPath, DatabaseKey.Generate());
        DatabaseKeyFile.Write(DbPath, NewKeyFile(DatabaseKey.Generate()));

        var ex = Assert.Throws<DatabaseOpenException>(() => new SqliteDatabase(DbPath));

        Assert.Equal(DatabaseProblem.KeyRequired, ex.Problem);
    }

    [Fact]
    public void PathOnlyConstructor_OnEncryptedFileWithoutKeyFile_ThrowsDamaged()
    {
        _ = new SqliteDatabase(DbPath, DatabaseKey.Generate());

        var ex = Assert.Throws<DatabaseOpenException>(() => new SqliteDatabase(DbPath));

        Assert.Equal(DatabaseProblem.Damaged, ex.Problem);
    }

    [Fact]
    public void KeyConstructor_OnPlainFile_ThrowsNotEncryptedAndLeavesFileUntouched()
    {
        CreatePlain();
        var before = File.ReadAllBytes(DbPath);

        var ex = Assert.Throws<DatabaseOpenException>(() => new SqliteDatabase(DbPath, DatabaseKey.Generate()));

        Assert.Equal(DatabaseProblem.NotEncrypted, ex.Problem);
        Assert.Equal(before, File.ReadAllBytes(DbPath));
    }

    [Fact]
    public void PathOnlyConstructor_StillCreatesPlainDatabase()
    {
        var database = new SqliteDatabase(DbPath);

        Assert.False(database.IsEncrypted);
        Assert.True(HasPlainHeader(DbPath));
    }

    [Fact]
    public void Migration_OfOlderEncryptedDatabase_MakesAnEncryptedBackupFirst()
    {
        var key = DatabaseKey.Generate();
        var first = new SqliteDatabase(DbPath, key);
        using (var connection = first.OpenConnection())
        {
            Exec(connection, "INSERT INTO stopwatches (id, name, color, sort_order) VALUES ('a', 'English', '#2F6FEB', 0)");
            Exec(connection, "DROP TABLE goal_resets");
            Exec(connection, "PRAGMA user_version = 13");
        }

        var migrated = new SqliteDatabase(DbPath, key);

        Assert.Equal(Path.Combine(BackupsFolder, "wowstopwatcher-v13.db"), migrated.BackupPath);
        Assert.Equal(SqliteDatabase.LatestSchemaVersion, migrated.GetSchemaVersion());
        Assert.Equal(DatabaseFileFormat.Encrypted, DatabaseFormatDetector.Detect(migrated.BackupPath!));
        using var backup = OpenRaw(migrated.BackupPath!, key);
        Assert.Equal(13L, Scalar(backup, "PRAGMA user_version"));
        Assert.Equal("English", Scalar(backup, "SELECT name FROM stopwatches"));
    }
}
