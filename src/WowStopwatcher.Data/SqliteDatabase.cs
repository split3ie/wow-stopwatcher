using Microsoft.Data.Sqlite;

namespace WowStopwatcher.Data;

/// <summary>
/// A SQLite database file. Creates the folder and file, enables WAL, and migrates the schema using
/// <c>PRAGMA user_version</c>. Every <see cref="OpenConnection"/> call returns a new, non-pooled connection
/// with foreign keys enabled; the caller disposes it.
/// </summary>
public sealed class SqliteDatabase
{
    /// <summary>Current schema version.</summary>
    public const int LatestSchemaVersion = 4;

    private readonly string _connectionString;

    /// <summary>Opens (creating if needed) the database at <paramref name="path"/> and migrates it.</summary>
    public SqliteDatabase(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        var folder = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();

        Migrate();
    }

    /// <summary>Full path of the database file.</summary>
    public string Path { get; }

    /// <summary>%AppData%\WowStopwatcher\wowstopwatcher.db</summary>
    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WowStopwatcher",
        "wowstopwatcher.db");

    /// <summary>Creates a database at <see cref="DefaultPath"/>.</summary>
    public static SqliteDatabase OpenDefault() => new(DefaultPath);

    /// <summary>Opens a new connection. Dispose it after use.</summary>
    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>The schema version stored in the file.</summary>
    public int GetSchemaVersion()
    {
        using var connection = OpenConnection();
        return ReadVersion(connection);
    }

    private void Migrate()
    {
        using var connection = OpenConnection();
        Execute(connection, "PRAGMA journal_mode = WAL;");

        var version = ReadVersion(connection);
        if (version > LatestSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Database schema version {version} is newer than this app supports ({LatestSchemaVersion}).");
        }

        if (version < 1)
        {
            using var transaction = connection.BeginTransaction();
            Execute(connection, """
                CREATE TABLE IF NOT EXISTS stopwatches (
                    id TEXT PRIMARY KEY NOT NULL,
                    name TEXT NOT NULL,
                    color TEXT NOT NULL,
                    goal_seconds INTEGER NULL,
                    shortcut TEXT NULL,
                    sort_order INTEGER NOT NULL,
                    is_deleted INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS sessions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    stopwatch_id TEXT NOT NULL REFERENCES stopwatches(id),
                    start_utc INTEGER NOT NULL,
                    stop_utc INTEGER NULL
                );
                CREATE INDEX IF NOT EXISTS ix_sessions_start_utc ON sessions(start_utc);
                """, transaction);
            Execute(connection, "PRAGMA user_version = 1;", transaction);
            transaction.Commit();
            version = 1;
        }

        if (version < 2)
        {
            using var transaction = connection.BeginTransaction();
            Execute(connection, "ALTER TABLE stopwatches ADD COLUMN is_independent INTEGER NOT NULL DEFAULT 0;", transaction);
            Execute(connection, "PRAGMA user_version = 2;", transaction);
            transaction.Commit();
            version = 2;
        }

        if (version < 3)
        {
            using var transaction = connection.BeginTransaction();
            Execute(connection, "CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL);", transaction);
            Execute(connection, "PRAGMA user_version = 3;", transaction);
            transaction.Commit();
            version = 3;
        }

        if (version < 4)
        {
            // Existing stopwatches become temporary; the migration time counts as their last use (grace period).
            using var transaction = connection.BeginTransaction();
            Execute(connection, "ALTER TABLE stopwatches ADD COLUMN is_permanent INTEGER NOT NULL DEFAULT 0;", transaction);
            Execute(connection, "ALTER TABLE stopwatches ADD COLUMN active_since_utc INTEGER NULL;", transaction);
            Execute(connection, "UPDATE stopwatches SET active_since_utc = CAST(strftime('%s', 'now') AS INTEGER) * 1000;", transaction);
            Execute(connection, "PRAGMA user_version = 4;", transaction);
            transaction.Commit();
        }
    }

    private static int ReadVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
