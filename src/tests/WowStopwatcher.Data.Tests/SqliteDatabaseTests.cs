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

            Assert.Equal(5, SqliteDatabase.LatestSchemaVersion);
            Assert.Equal(5, database.GetSchemaVersion());
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
