using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using WowStopwatcher.Core;

namespace WowStopwatcher.Data.Tests;

public class SqliteDatabaseTests
{
    [Fact]
    public void CreatesFolderFileAndSchema()
    {
        using var db = new TempDatabase();
        Assert.True(File.Exists(db.Database.Path));
        Assert.Equal(SqliteDatabase.LatestSchemaVersion, db.Database.GetSchemaVersion());
    }

    [Fact]
    public void Migration_IsIdempotent_AndKeepsData()
    {
        using var db = new TempDatabase();
        db.AddStopwatch("a");

        var again = new SqliteDatabase(db.Database.Path);
        var again2 = new SqliteDatabase(db.Database.Path);

        Assert.Equal(SqliteDatabase.LatestSchemaVersion, again2.GetSchemaVersion());
        Assert.Single(new SqliteStopwatchRepository(again).GetAll());
    }

    [Fact]
    public void Migration_V1ToV2_KeepsRowsAndAddsIndependentColumn()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "v1.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE stopwatches (
                        id TEXT PRIMARY KEY NOT NULL, name TEXT NOT NULL, color TEXT NOT NULL,
                        goal_seconds INTEGER NULL, shortcut TEXT NULL, sort_order INTEGER NOT NULL,
                        is_deleted INTEGER NOT NULL DEFAULT 0);
                    CREATE TABLE sessions (
                        id INTEGER PRIMARY KEY AUTOINCREMENT, stopwatch_id TEXT NOT NULL REFERENCES stopwatches(id),
                        start_utc INTEGER NOT NULL, stop_utc INTEGER NULL);
                    INSERT INTO stopwatches VALUES ('a', 'English', '#2F6FEB', 18000, 'Ctrl+F1', 0, 0);
                    INSERT INTO sessions (stopwatch_id, start_utc, stop_utc) VALUES ('a', 1000, 2000);
                    PRAGMA user_version = 1;
                    """;
                command.ExecuteNonQuery();
            }

            var database = new SqliteDatabase(path);

            Assert.Equal(SqliteDatabase.LatestSchemaVersion, database.GetSchemaVersion());
            var def = Assert.Single(new SqliteStopwatchRepository(database).GetAll());
            Assert.Equal(new StopwatchDefinition("a", "English", "#2F6FEB", TimeSpan.FromHours(5), "Ctrl+F1", 0, ActiveSinceUtc: def.ActiveSinceUtc), def);
            Assert.Single(new SqliteSessionRepository(database).GetInRange(DateTimeOffset.FromUnixTimeMilliseconds(0), DateTimeOffset.FromUnixTimeMilliseconds(5000)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Migration_V3ToV4_AddsPermanentAndActiveSinceColumns()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "v3.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE stopwatches (
                        id TEXT PRIMARY KEY NOT NULL, name TEXT NOT NULL, color TEXT NOT NULL,
                        goal_seconds INTEGER NULL, shortcut TEXT NULL, sort_order INTEGER NOT NULL,
                        is_deleted INTEGER NOT NULL DEFAULT 0, is_independent INTEGER NOT NULL DEFAULT 0);
                    CREATE TABLE sessions (
                        id INTEGER PRIMARY KEY AUTOINCREMENT, stopwatch_id TEXT NOT NULL REFERENCES stopwatches(id),
                        start_utc INTEGER NOT NULL, stop_utc INTEGER NULL);
                    CREATE TABLE settings (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL);
                    INSERT INTO stopwatches VALUES ('a', 'English', '#2F6FEB', 18000, 'Ctrl+F1', 0, 0, 0);
                    INSERT INTO stopwatches VALUES ('b', 'Old', '#111111', NULL, NULL, 1, 1, 1);
                    PRAGMA user_version = 3;
                    """;
                command.ExecuteNonQuery();
            }

            var before = DateTimeOffset.UtcNow.AddSeconds(-2);
            var database = new SqliteDatabase(path);
            var after = DateTimeOffset.UtcNow.AddSeconds(2);

            Assert.Equal(14, SqliteDatabase.LatestSchemaVersion);
            Assert.Equal(SqliteDatabase.LatestSchemaVersion, database.GetSchemaVersion());
            var all = new SqliteStopwatchRepository(database).GetAll(includeDeleted: true);
            Assert.Equal(2, all.Count);
            Assert.All(all, d =>
            {
                Assert.False(d.IsPermanent);
                Assert.NotNull(d.ActiveSinceUtc);
                Assert.InRange(d.ActiveSinceUtc!.Value, before, after);
            });
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Migration_V5ToV6_AddsGoalSinceColumnAsNull()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "v5.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE stopwatches (
                        id TEXT PRIMARY KEY NOT NULL, name TEXT NOT NULL, color TEXT NOT NULL,
                        goal_seconds INTEGER NULL, shortcut TEXT NULL, sort_order INTEGER NOT NULL,
                        is_deleted INTEGER NOT NULL DEFAULT 0, is_independent INTEGER NOT NULL DEFAULT 0,
                        is_permanent INTEGER NOT NULL DEFAULT 0, active_since_utc INTEGER NULL);
                    CREATE TABLE sessions (
                        id INTEGER PRIMARY KEY AUTOINCREMENT, stopwatch_id TEXT NOT NULL REFERENCES stopwatches(id),
                        start_utc INTEGER NOT NULL, stop_utc INTEGER NULL);
                    CREATE TABLE settings (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL);
                    CREATE TABLE goal_changes (
                        id INTEGER PRIMARY KEY AUTOINCREMENT, stopwatch_id TEXT NOT NULL REFERENCES stopwatches(id),
                        changed_utc INTEGER NOT NULL, previous_goal_seconds INTEGER NULL);
                    INSERT INTO stopwatches VALUES ('a', 'English', '#2F6FEB', 18000, 'Ctrl+F1', 0, 0, 0, 1, 1000);
                    PRAGMA user_version = 5;
                    """;
                command.ExecuteNonQuery();
            }

            var database = new SqliteDatabase(path);

            Assert.Equal(SqliteDatabase.LatestSchemaVersion, database.GetSchemaVersion());
            var def = Assert.Single(new SqliteStopwatchRepository(database).GetAll());
            Assert.Equal("English", def.Name);
            Assert.Equal(TimeSpan.FromHours(5), def.Goal);
            Assert.Null(def.GoalSinceUtc);
            Assert.False(def.GoalPerCycle);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Migration_V6ToV7_KeepsNamesWithUnknownLanguage()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "v6.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE stopwatches (
                        id TEXT PRIMARY KEY NOT NULL, name TEXT NOT NULL, color TEXT NOT NULL,
                        goal_seconds INTEGER NULL, shortcut TEXT NULL, sort_order INTEGER NOT NULL,
                        is_deleted INTEGER NOT NULL DEFAULT 0, is_independent INTEGER NOT NULL DEFAULT 0,
                        is_permanent INTEGER NOT NULL DEFAULT 0, active_since_utc INTEGER NULL,
                        goal_since_utc INTEGER NULL, goal_per_cycle INTEGER NOT NULL DEFAULT 0);
                    CREATE TABLE sessions (
                        id INTEGER PRIMARY KEY AUTOINCREMENT, stopwatch_id TEXT NOT NULL REFERENCES stopwatches(id),
                        start_utc INTEGER NOT NULL, stop_utc INTEGER NULL);
                    CREATE TABLE settings (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL);
                    CREATE TABLE goal_changes (
                        id INTEGER PRIMARY KEY AUTOINCREMENT, stopwatch_id TEXT NOT NULL REFERENCES stopwatches(id),
                        changed_utc INTEGER NOT NULL, previous_goal_seconds INTEGER NULL);
                    INSERT INTO stopwatches VALUES ('a', 'English', '#2F6FEB', 18000, 'Ctrl+F1', 0, 0, 0, 1, 1000, NULL, 0);
                    PRAGMA user_version = 6;
                    """;
                command.ExecuteNonQuery();
            }

            var database = new SqliteDatabase(path);

            Assert.Equal(SqliteDatabase.LatestSchemaVersion, database.GetSchemaVersion());
            Assert.Equal("English", Assert.Single(new SqliteStopwatchRepository(database).GetAll()).Name);
            var uk = new SqliteStopwatchRepository(database, null, () => "uk");
            Assert.Equal("English", Assert.Single(uk.GetAll()).Name);

            // The first rename claims the original and sets its language.
            uk.Update(uk.Get("a")! with { Name = "Anhliiska" });
            Assert.Equal("Anhliiska", new SqliteStopwatchRepository(database, null, () => "en").Get("a")!.Name);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private const string V6Schema = """
        CREATE TABLE stopwatches (
            id TEXT PRIMARY KEY NOT NULL, name TEXT NOT NULL, color TEXT NOT NULL,
            goal_seconds INTEGER NULL, shortcut TEXT NULL, sort_order INTEGER NOT NULL,
            is_deleted INTEGER NOT NULL DEFAULT 0, is_independent INTEGER NOT NULL DEFAULT 0,
            is_permanent INTEGER NOT NULL DEFAULT 0, active_since_utc INTEGER NULL,
            goal_since_utc INTEGER NULL, goal_per_cycle INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE sessions (
            id INTEGER PRIMARY KEY AUTOINCREMENT, stopwatch_id TEXT NOT NULL REFERENCES stopwatches(id),
            start_utc INTEGER NOT NULL, stop_utc INTEGER NULL);
        CREATE TABLE settings (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL);
        CREATE TABLE goal_changes (
            id INTEGER PRIMARY KEY AUTOINCREMENT, stopwatch_id TEXT NOT NULL REFERENCES stopwatches(id),
            changed_utc INTEGER NOT NULL, previous_goal_seconds INTEGER NULL);
        INSERT INTO stopwatches VALUES ('a', 'English', '#2F6FEB', 18000, 'Ctrl+F1', 0, 0, 0, 1, 1000, NULL, 0);
        PRAGMA user_version = 6;
        """;

    private static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void CreateV6(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = V6Schema;
        command.ExecuteNonQuery();
    }

    private static long ReadOnlyScalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    [Fact]
    public void Migration_FromOlderVersion_MakesVerifiedBackupFirst_AndOnlyOnce()
    {
        var folder = NewFolder();
        var path = Path.Combine(folder, "v6.db");
        try
        {
            CreateV6(path);

            var database = new SqliteDatabase(path);

            var expected = Path.Combine(folder, "backups", "v6-v6.db");
            Assert.Equal(expected, database.BackupPath);
            Assert.True(File.Exists(expected));
            Assert.Equal(6, ReadOnlyScalar(expected, "PRAGMA user_version;"));
            Assert.Equal(1, ReadOnlyScalar(expected, "SELECT COUNT(*) FROM stopwatches;"));
            Assert.Equal(SqliteDatabase.LatestSchemaVersion, database.GetSchemaVersion());

            var again = new SqliteDatabase(path);

            Assert.Null(again.BackupPath);
            Assert.Single(Directory.GetFiles(Path.Combine(folder, "backups")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Backup_DoesNotOverwriteExistingBackup()
    {
        var folder = NewFolder();
        var path = Path.Combine(folder, "v6.db");
        try
        {
            var backups = Path.Combine(folder, "backups");
            Directory.CreateDirectory(backups);
            var existing = Path.Combine(backups, "v6-v6.db");
            File.WriteAllText(existing, "keep me");
            CreateV6(path);

            var database = new SqliteDatabase(path);

            Assert.Equal(Path.Combine(backups, "v6-v6-2.db"), database.BackupPath);
            Assert.Equal("keep me", File.ReadAllText(existing));
            Assert.Equal(6, ReadOnlyScalar(database.BackupPath!, "PRAGMA user_version;"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void NewFile_MakesNoBackup()
    {
        var folder = NewFolder();
        try
        {
            var database = new SqliteDatabase(Path.Combine(folder, "new.db"));

            Assert.Null(database.BackupPath);
            Assert.False(Directory.Exists(Path.Combine(folder, "backups")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void BackupFailure_Throws_AndDoesNotMigrate()
    {
        var folder = NewFolder();
        var path = Path.Combine(folder, "v6.db");
        try
        {
            CreateV6(path);
            File.WriteAllText(Path.Combine(folder, "backups"), "not a folder");

            var ex = Assert.Throws<DatabaseOpenException>(() => new SqliteDatabase(path));

            Assert.Equal(DatabaseProblem.BackupFailed, ex.Problem);
            Assert.Equal(6, ReadOnlyScalar(path, "PRAGMA user_version;"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void NewerSchemaVersion_Throws_AndLeavesFileUntouched()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "newer.db");
        try
        {
            var newer = SqliteDatabase.LatestSchemaVersion + 1;
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = $"CREATE TABLE future (id INTEGER); INSERT INTO future VALUES (1); PRAGMA user_version = {newer};";
                command.ExecuteNonQuery();
            }

            var before = SHA256.HashData(File.ReadAllBytes(path));

            var ex = Assert.Throws<DatabaseOpenException>(() => new SqliteDatabase(path));

            Assert.Equal(DatabaseProblem.NewerVersion, ex.Problem);
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.False(File.Exists(path + "-wal"));
            using var check = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
            check.Open();
            using var read = check.CreateCommand();
            read.CommandText = "PRAGMA user_version;";
            Assert.Equal((long)newer, Convert.ToInt64(read.ExecuteScalar()));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void NonSqliteFile_ThrowsDamaged_AndLeavesFileUntouched()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "junk.db");
        try
        {
            var bytes = new byte[4096];
            new Random(42).NextBytes(bytes);
            File.WriteAllBytes(path, bytes);

            var ex = Assert.Throws<DatabaseOpenException>(() => new SqliteDatabase(path));

            Assert.Equal(DatabaseProblem.Damaged, ex.Problem);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void MigratingFromV8_KeepsPlaceholdersAndAllowsAutomaticKind()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "old.db");
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE companies (id TEXT PRIMARY KEY NOT NULL);
                    CREATE TABLE invoices (id INTEGER PRIMARY KEY);
                    CREATE TABLE invoice_placeholders (
                        id INTEGER PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
                        alias TEXT NOT NULL, kind TEXT NOT NULL CHECK (kind IN ('static','script')), value TEXT NOT NULL DEFAULT '',
                        hidden INTEGER NOT NULL DEFAULT 0, sort_order INTEGER NOT NULL, UNIQUE (company_id, alias));
                    INSERT INTO companies VALUES ('c');
                    INSERT INTO invoice_placeholders VALUES (1, 'c', 'a', 'script', 'return 1', 1, 0);
                    PRAGMA user_version = 8;
                    """;
                command.ExecuteNonQuery();
            }

            var database = new SqliteDatabase(path);

            Assert.Equal(SqliteDatabase.LatestSchemaVersion, database.GetSchemaVersion());
            Assert.NotNull(database.BackupPath);
            using var check = database.OpenConnection();
            using var read = check.CreateCommand();
            read.CommandText = "SELECT alias, kind, value, hidden FROM invoice_placeholders";
            using (var reader = read.ExecuteReader())
            {
                Assert.True(reader.Read());
                Assert.Equal(("a", "script", "return 1", 1L), (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3)));
            }

            using var insert = check.CreateCommand();
            insert.CommandText = "INSERT INTO invoice_placeholders (company_id, alias, kind, sort_order) VALUES ('c', 'hours', 'automatic', 1)";
            Assert.Equal(1, insert.ExecuteNonQuery());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void MigratingFromV11_AddsTheLineMovesTable()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "v11.db");
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE companies (id TEXT PRIMARY KEY);
                    CREATE TABLE invoice_lines (id INTEGER PRIMARY KEY);
                    INSERT INTO invoice_lines VALUES (1);
                    PRAGMA user_version = 11;
                    """;
                command.ExecuteNonQuery();
            }

            var database = new SqliteDatabase(path);

            Assert.Equal(SqliteDatabase.LatestSchemaVersion, database.GetSchemaVersion());
            Assert.NotNull(database.BackupPath);
            using var check = database.OpenConnection();
            using var insert = check.CreateCommand();
            insert.CommandText = "INSERT INTO invoice_line_moves (line_id, from_date, minutes, sort_order) VALUES (1, '2026-11-03', 240, 0)";
            Assert.Equal(1, insert.ExecuteNonQuery());
            using var delete = check.CreateCommand();
            delete.CommandText = "DELETE FROM invoice_lines WHERE id = 1; SELECT COUNT(*) FROM invoice_line_moves";
            Assert.Equal(0L, Convert.ToInt64(delete.ExecuteScalar()));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Migration_V13ToV14_CreatesGoalResetsTable()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "v13.db");
        try
        {
            var seed = new SqliteDatabase(path);
            using (var connection = seed.OpenConnection())
            {
                using var command = connection.CreateCommand();
                command.CommandText = "DROP TABLE goal_resets; PRAGMA user_version = 13;";
                command.ExecuteNonQuery();
            }

            SqliteConnection.ClearAllPools();
            var database = new SqliteDatabase(path);

            Assert.Equal(SqliteDatabase.LatestSchemaVersion, database.GetSchemaVersion());
            var repository = new SqliteStopwatchRepository(database);
            repository.Add(new StopwatchDefinition("a", "A", "#111111", null, null, 0));
            repository.AddGoalReset(new GoalReset("a", DateTimeOffset.FromUnixTimeMilliseconds(5000)));
            Assert.Single(repository.GetGoalResets());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ForeignKeysAreEnforced()
    {
        using var db = new TempDatabase();
        Assert.ThrowsAny<Exception>(() => db.Sessions.InsertOpen("missing", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void DefaultPath_IsInAppData()
    {
        Assert.EndsWith(Path.Combine("WowStopwatcher", "wowstopwatcher.db"), SqliteDatabase.DefaultPath);
    }
}
