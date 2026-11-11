using Microsoft.Data.Sqlite;

namespace WowStopwatcher.Data;

/// <summary>
/// A SQLite database file. An existing file is first inspected read-only (newer schema, damage); if it cannot be used
/// a <see cref="DatabaseOpenException"/> is thrown and the file is left untouched. An existing database of an older schema
/// is backed up and verified (<c>backups/&lt;name&gt;-v&lt;old&gt;.db</c>) before it is migrated; if that fails nothing is migrated.
/// Otherwise creates the folder and file, enables WAL, and migrates the schema using <c>PRAGMA user_version</c>. Every <see cref="OpenConnection"/> call returns a new, non-pooled connection
/// with foreign keys enabled; the caller disposes it.
/// </summary>
public sealed class SqliteDatabase
{
    /// <summary>Current schema version.</summary>
    public const int LatestSchemaVersion = 7;

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

        if (File.Exists(Path))
        {
            var version = Inspect();
            if (version is >= 1 and < LatestSchemaVersion)
            {
                BackupPath = Backup(version);
            }
        }

        Migrate();
    }

    /// <summary>Full path of the database file.</summary>
    public string Path { get; }

    /// <summary>Full path of the backup made before migrating, or null when none was needed.</summary>
    public string? BackupPath { get; }

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

    /// <summary>Checks an existing file with a read-only connection, so nothing is written. Returns its schema version.</summary>
    private int Inspect()
    {
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var version = ReadVersion(connection);
            if (version > LatestSchemaVersion)
            {
                throw NewerVersion(version);
            }

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            var result = command.ExecuteScalar() as string;
            if (result != "ok")
            {
                throw new DatabaseOpenException(DatabaseProblem.Damaged, Path, $"Database integrity check failed: {result}");
            }

            return version;
        }
        catch (DatabaseOpenException)
        {
            throw;
        }
        catch (SqliteException ex)
        {
            throw Classify(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DatabaseOpenException(DatabaseProblem.Unavailable, Path, "The database file could not be opened.", ex);
        }
    }

    /// <summary>Copies the database to <c>backups/&lt;name&gt;-v&lt;version&gt;.db</c> and verifies the copy. Never overwrites a file.</summary>
    private string Backup(int version)
    {
        string? created = null;
        try
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "backups");
            Directory.CreateDirectory(folder);

            var baseName = $"{System.IO.Path.GetFileNameWithoutExtension(Path)}-v{version}";
            var target = System.IO.Path.Combine(folder, baseName + ".db");
            for (var n = 2; File.Exists(target); n++)
            {
                target = System.IO.Path.Combine(folder, $"{baseName}-{n}.db");
            }

            created = target;
            using (var source = new SqliteConnection(ConnectionString(Path, SqliteOpenMode.ReadOnly)))
            using (var destination = new SqliteConnection(ConnectionString(target, SqliteOpenMode.ReadWriteCreate)))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }

            using (var check = new SqliteConnection(ConnectionString(target, SqliteOpenMode.ReadOnly)))
            {
                check.Open();
                var backupVersion = ReadVersion(check);
                if (backupVersion != version)
                {
                    throw new InvalidDataException($"Backup has schema version {backupVersion}, expected {version}.");
                }

                using var command = check.CreateCommand();
                command.CommandText = "PRAGMA quick_check;";
                var result = command.ExecuteScalar() as string;
                if (result != "ok")
                {
                    throw new InvalidDataException($"Backup integrity check failed: {result}");
                }
            }

            return target;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (created is not null)
            {
                try
                {
                    File.Delete(created);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    // Best effort: a partial backup we could not remove is left in place.
                }
            }

            throw new DatabaseOpenException(DatabaseProblem.BackupFailed, Path, "A backup could not be made before updating the database.", ex);
        }
    }

    private static string ConnectionString(string file, SqliteOpenMode mode) => new SqliteConnectionStringBuilder
    {
        DataSource = file,
        Mode = mode,
        Pooling = false,
    }.ToString();

    private DatabaseOpenException NewerVersion(int version) => new(
        DatabaseProblem.NewerVersion,
        Path,
        $"Database schema version {version} is newer than this app supports ({LatestSchemaVersion}).");

    private DatabaseOpenException Classify(SqliteException ex) => ex.SqliteErrorCode is 11 or 26
        ? new DatabaseOpenException(DatabaseProblem.Damaged, Path, "The database file is damaged.", ex)
        : new DatabaseOpenException(DatabaseProblem.Unavailable, Path, "The database file could not be opened.", ex);

    private void Migrate()
    {
        try
        {
            MigrateCore();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 11 or 26)
        {
            throw Classify(ex);
        }
    }

    private void MigrateCore()
    {
        using var connection = OpenConnection();

        var version = ReadVersion(connection);
        if (version > LatestSchemaVersion)
        {
            throw NewerVersion(version);
        }

        Execute(connection, "PRAGMA journal_mode = WAL;");

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
            version = 4;
        }

        if (version < 5)
        {
            using var transaction = connection.BeginTransaction();
            Execute(connection, """
                CREATE TABLE goal_changes (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    stopwatch_id TEXT NOT NULL REFERENCES stopwatches(id),
                    changed_utc INTEGER NOT NULL,
                    previous_goal_seconds INTEGER NULL);
                """, transaction);
            Execute(connection, "CREATE INDEX idx_goal_changes_stopwatch ON goal_changes (stopwatch_id);", transaction);
            Execute(connection, "PRAGMA user_version = 5;", transaction);
            transaction.Commit();
            version = 5;
        }

        if (version < 6)
        {
            using var transaction = connection.BeginTransaction();
            Execute(connection, "ALTER TABLE stopwatches ADD COLUMN goal_since_utc INTEGER NULL;", transaction);
            Execute(connection, "ALTER TABLE stopwatches ADD COLUMN goal_per_cycle INTEGER NOT NULL DEFAULT 0;", transaction);
            Execute(connection, "PRAGMA user_version = 6;", transaction);
            transaction.Commit();
            version = 6;
        }

        if (version < 7)
        {
            // name_language is the language of stopwatches.name (NULL = unknown, legacy rows).
            using var transaction = connection.BeginTransaction();
            Execute(connection, "ALTER TABLE stopwatches ADD COLUMN name_language TEXT NULL;", transaction);
            Execute(connection, """
                CREATE TABLE stopwatch_names (
                    stopwatch_id TEXT NOT NULL REFERENCES stopwatches(id),
                    language TEXT NOT NULL,
                    name TEXT NOT NULL,
                    PRIMARY KEY (stopwatch_id, language));
                """, transaction);
            Execute(connection, "PRAGMA user_version = 7;", transaction);
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
