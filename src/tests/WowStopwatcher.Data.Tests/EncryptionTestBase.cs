using Microsoft.Data.Sqlite;
using WowStopwatcher.Core.Security;

namespace WowStopwatcher.Data.Tests;

/// <summary>A unique temp folder per test (never the real AppData) with helpers to build plain and encrypted databases.</summary>
public abstract class EncryptionTestBase : IDisposable
{
    protected EncryptionTestBase()
    {
        Folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        DbPath = Path.Combine(Folder, "wowstopwatcher.db");
    }

    protected string Folder { get; }

    protected string DbPath { get; }

    protected string BackupsFolder => Path.Combine(Folder, "backups");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(Folder))
        {
            Directory.Delete(Folder, recursive: true);
        }
    }

    protected static DatabaseKeyFileContents NewKeyFile(DatabaseKey key) =>
        new(Guid.NewGuid(), DatabaseKeyWrapper.Wrap(key, RecoveryCode.Generate(), iterations: 1000));

    /// <summary>Creates a plain database with a stopwatch and two sessions.</summary>
    protected void CreatePlain(string? path = null)
    {
        var database = new SqliteDatabase(path ?? DbPath);
        using var connection = database.OpenConnection();
        Exec(connection, "INSERT INTO stopwatches (id, name, color, sort_order) VALUES ('a', 'English', '#2F6FEB', 0)");
        Exec(connection, "INSERT INTO sessions (stopwatch_id, start_utc, stop_utc) VALUES ('a', 1000, 2000), ('a', 3000, NULL)");
    }

    protected static long CountSessions(SqliteDatabase database)
    {
        using var connection = database.OpenConnection();
        return Convert.ToInt64(Scalar(connection, "SELECT count(*) FROM sessions"));
    }

    /// <summary>Opens a file with the raw-key pragmas written out here, independent of the production helper.</summary>
    protected static SqliteConnection OpenRaw(string path, DatabaseKey? key)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        if (key is not null)
        {
            Exec(connection, "PRAGMA cipher = 'chacha20';");
            Exec(connection, $"PRAGMA key = \"x'{key.ToHex()}'\";");
        }

        return connection;
    }

    protected static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    protected static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    protected static bool HasPlainHeader(string path) => DatabaseFormatDetector.Detect(path) == DatabaseFileFormat.Plain;
}
