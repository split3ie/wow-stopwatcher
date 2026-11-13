using Microsoft.Data.Sqlite;
using WowStopwatcher.Core.Security;

namespace WowStopwatcher.Data;

/// <summary>
/// A SQLite database file. An existing file is first inspected read-only (newer schema, damage); if it cannot be used
/// a <see cref="DatabaseOpenException"/> is thrown and the file is left untouched. An existing database of an older schema
/// is backed up and verified (<c>backups/&lt;name&gt;-v&lt;old&gt;.db</c>) before it is migrated; if that fails nothing is migrated.
/// Otherwise creates the folder and file, enables WAL, and migrates the schema using <c>PRAGMA user_version</c>. Every <see cref="OpenConnection"/> call returns a new, non-pooled connection
/// with foreign keys enabled; the caller disposes it. With a <see cref="DatabaseKey"/> the file is encrypted (see <see cref="DatabaseConnections"/>);
/// without one it must be a plain database, and the format of an existing file is checked first (<see cref="DatabaseProblem.KeyRequired"/>,
/// <see cref="DatabaseProblem.NotEncrypted"/>, <see cref="DatabaseProblem.KeyRejected"/>).
/// </summary>
public sealed class SqliteDatabase
{
    /// <summary>Current schema version.</summary>
    public const int LatestSchemaVersion = 14;

    private readonly DatabaseKey? _key;

    /// <summary>Opens (creating if needed) the plain database at <paramref name="path"/> and migrates it.</summary>
    public SqliteDatabase(string path)
        : this(path, null, true)
    {
    }

    /// <summary>Opens (creating if needed) the encrypted database at <paramref name="path"/> with <paramref name="key"/> and migrates it.</summary>
    public SqliteDatabase(string path, DatabaseKey key)
        : this(path, key, true)
    {
    }

    private SqliteDatabase(string path, DatabaseKey? key, bool _)
    {
        _key = key;
        Path = System.IO.Path.GetFullPath(path);
        var folder = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        CheckFormat();

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

    /// <summary>True when the database is opened with a key.</summary>
    public bool IsEncrypted => _key is not null;

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
        return DatabaseConnections.Open(Path, SqliteOpenMode.ReadWriteCreate, _key, foreignKeys: true);
    }

    /// <summary>Compares the file header with what this instance was asked to open, before SQLite touches the file.</summary>
    private void CheckFormat()
    {
        DatabaseFileFormat format;
        try
        {
            format = DatabaseFormatDetector.Detect(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DatabaseOpenException(DatabaseProblem.Unavailable, Path, "The database file could not be opened.", ex);
        }

        // A plain open of a non-SQLite file stays "Damaged"; only a file with a key file next to it is taken for an encrypted database.
        if (_key is null && format == DatabaseFileFormat.Encrypted && File.Exists(DatabaseKeyFile.PathFor(Path)))
        {
            throw new DatabaseOpenException(DatabaseProblem.KeyRequired, Path, "The database file is encrypted and no key was given.");
        }

        if (_key is not null && format == DatabaseFileFormat.Plain)
        {
            throw new DatabaseOpenException(DatabaseProblem.NotEncrypted, Path, "The database file is not encrypted.");
        }
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
            using var connection = DatabaseConnections.Open(Path, SqliteOpenMode.ReadOnly, _key);

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

    /// <summary>
    /// Copies the database to <c>backups/&lt;name&gt;-v&lt;version&gt;.db</c> and verifies the copy. Never overwrites a file.
    /// An encrypted database is copied with the same key and its key file goes next to the copy (<see cref="DatabaseKeyFile.PathFor"/>).
    /// </summary>
    private string Backup(int version)
    {
        string? created = null;
        string? createdKeyFile = null;
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
            using (var source = DatabaseConnections.Open(Path, SqliteOpenMode.ReadOnly, _key))
            using (var destination = DatabaseConnections.Open(target, SqliteOpenMode.ReadWriteCreate, _key))
            {
                source.BackupDatabase(destination);
            }

            using (var check = DatabaseConnections.Open(target, SqliteOpenMode.ReadOnly, _key))
            {
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

            var sourceKeyFile = DatabaseKeyFile.PathFor(Path);
            if (_key is not null && File.Exists(sourceKeyFile))
            {
                createdKeyFile = DatabaseKeyFile.PathFor(target);
                File.Copy(sourceKeyFile, createdKeyFile, overwrite: false);
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
                    if (createdKeyFile is not null)
                    {
                        File.Delete(createdKeyFile);
                    }
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    // Best effort: a partial backup we could not remove is left in place.
                }
            }

            throw new DatabaseOpenException(DatabaseProblem.BackupFailed, Path, "A backup could not be made before updating the database.", ex);
        }
    }

    private DatabaseOpenException NewerVersion(int version) => new(
        DatabaseProblem.NewerVersion,
        Path,
        $"Database schema version {version} is newer than this app supports ({LatestSchemaVersion}).");

    private DatabaseOpenException Classify(SqliteException ex) => (ex.SqliteErrorCode, _key) switch
    {
        // An encrypted file that does not decode: the key is wrong (a damaged encrypted file looks the same).
        (26, not null) => new DatabaseOpenException(DatabaseProblem.KeyRejected, Path, "The database key was rejected.", ex),
        (11 or 26, _) => new DatabaseOpenException(DatabaseProblem.Damaged, Path, "The database file is damaged.", ex),
        _ => new DatabaseOpenException(DatabaseProblem.Unavailable, Path, "The database file could not be opened.", ex),
    };

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
            version = 7;
        }

        if (version < 8)
        {
            // Invoicing: companies, their stopwatches and placeholders, invoices with stored days and lines.
            using var transaction = connection.BeginTransaction();
            Execute(connection, """
                CREATE TABLE companies (
                    id TEXT PRIMARY KEY NOT NULL, name TEXT NOT NULL, color TEXT NOT NULL, sort_order INTEGER NOT NULL,
                    rate TEXT NOT NULL DEFAULT '0', currency TEXT NOT NULL DEFAULT 'USD', template_file TEXT NULL, template_name TEXT NULL,
                    template_sha256 TEXT NULL, file_name_pattern TEXT NOT NULL, ask_destination INTEGER NOT NULL DEFAULT 1, output_folder TEXT NULL,
                    next_invoice_number INTEGER NOT NULL DEFAULT 1, created_utc INTEGER NOT NULL, deleted_utc INTEGER NULL);
                CREATE TABLE company_stopwatches (
                    company_id TEXT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
                    stopwatch_id TEXT NOT NULL UNIQUE, PRIMARY KEY (company_id, stopwatch_id));
                CREATE TABLE invoice_placeholders (
                    id INTEGER PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
                    alias TEXT NOT NULL, kind TEXT NOT NULL CHECK (kind IN ('static','script')), value TEXT NOT NULL DEFAULT '',
                    hidden INTEGER NOT NULL DEFAULT 0, sort_order INTEGER NOT NULL, UNIQUE (company_id, alias));
                CREATE TABLE invoices (
                    id INTEGER PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id), year INTEGER NOT NULL,
                    month INTEGER NOT NULL, status TEXT NOT NULL CHECK (status IN ('pending','sent')), number INTEGER NULL, file_path TEXT NULL,
                    generated_utc INTEGER NULL, generated_input_hash TEXT NULL, sent_utc INTEGER NULL, snapshot_json TEXT NULL,
                    UNIQUE (company_id, year, month));
                CREATE TABLE invoice_days (
                    invoice_id INTEGER NOT NULL REFERENCES invoices(id) ON DELETE CASCADE, local_date TEXT NOT NULL,
                    edited INTEGER NOT NULL, PRIMARY KEY (invoice_id, local_date));
                CREATE TABLE invoice_lines (
                    id INTEGER PRIMARY KEY, invoice_id INTEGER NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
                    local_date TEXT NOT NULL, minutes INTEGER NOT NULL CHECK (minutes > 0), note TEXT NOT NULL DEFAULT '', sort_order INTEGER NOT NULL);
                CREATE TABLE invoice_line_sources (
                    line_id INTEGER NOT NULL REFERENCES invoice_lines(id) ON DELETE CASCADE,
                    stopwatch_id TEXT NOT NULL, stopwatch_name TEXT NOT NULL, sort_order INTEGER NOT NULL, PRIMARY KEY (line_id, stopwatch_id));
                """, transaction);
            Execute(connection, "PRAGMA user_version = 8;", transaction);
            transaction.Commit();
            version = 8;
        }

        if (version < 9)
        {
            // Placeholder kind 'automatic' (the built-in value). SQLite cannot change a CHECK constraint, so the table is rebuilt.
            using var transaction = connection.BeginTransaction();
            Execute(connection, """
                CREATE TABLE invoice_placeholders_new (
                    id INTEGER PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
                    alias TEXT NOT NULL, kind TEXT NOT NULL CHECK (kind IN ('static','script','automatic')), value TEXT NOT NULL DEFAULT '',
                    hidden INTEGER NOT NULL DEFAULT 0, sort_order INTEGER NOT NULL, UNIQUE (company_id, alias));
                INSERT INTO invoice_placeholders_new (id, company_id, alias, kind, value, hidden, sort_order)
                    SELECT id, company_id, alias, kind, value, hidden, sort_order FROM invoice_placeholders;
                DROP TABLE invoice_placeholders;
                ALTER TABLE invoice_placeholders_new RENAME TO invoice_placeholders;
                """, transaction);
            Execute(connection, "PRAGMA user_version = 9;", transaction);
            transaction.Commit();
            version = 9;
        }

        if (version < 10)
        {
            // Invoice day start: a default per company and a start for single dates (a start also ends the previous day).
            // invoice_day_starts is not read or written yet: only the company default is used.
            using var transaction = connection.BeginTransaction();
            Execute(connection, """
                ALTER TABLE companies ADD COLUMN day_start_minutes INTEGER NOT NULL DEFAULT 0;
                CREATE TABLE invoice_day_starts (
                    company_id TEXT NOT NULL REFERENCES companies(id) ON DELETE CASCADE, date TEXT NOT NULL,
                    start_minutes INTEGER NOT NULL CHECK (start_minutes BETWEEN 0 AND 1439), PRIMARY KEY (company_id, date));
                """, transaction);
            Execute(connection, "PRAGMA user_version = 10;", transaction);
            transaction.Commit();
            version = 10;
        }

        if (version < 11)
        {
            // Reported time for a month: replaces the sum of the billed lines when set.
            using var transaction = connection.BeginTransaction();
            Execute(connection, "ALTER TABLE invoices ADD COLUMN reported_minutes INTEGER NULL CHECK (reported_minutes > 0);", transaction);
            Execute(connection, "PRAGMA user_version = 11;", transaction);
            transaction.Commit();
            version = 11;
        }

        if (version < 12)
        {
            // Time a line holds from other days (merged across days), so moving time does not show as drift.
            using var transaction = connection.BeginTransaction();
            Execute(connection, """
                CREATE TABLE invoice_line_moves (
                    line_id INTEGER NOT NULL REFERENCES invoice_lines(id) ON DELETE CASCADE,
                    from_date TEXT NOT NULL, minutes INTEGER NOT NULL CHECK (minutes > 0), sort_order INTEGER NOT NULL,
                    PRIMARY KEY (line_id, sort_order));
                """, transaction);
            Execute(connection, "PRAGMA user_version = 12;", transaction);
            transaction.Commit();
            version = 12;
        }

        if (version < 13)
        {
            // Date format for the date values of a company's invoices (null: the app language's short date).
            using var transaction = connection.BeginTransaction();
            Execute(connection, "ALTER TABLE companies ADD COLUMN date_format TEXT NULL;", transaction);
            Execute(connection, "PRAGMA user_version = 13;", transaction);
            transaction.Commit();
            version = 13;
        }

        if (version < 14)
        {
            // Manual resets of per-cycle goals: each cancels the open cycle and starts a new one.
            using var transaction = connection.BeginTransaction();
            Execute(connection, """
                CREATE TABLE goal_resets (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    stopwatch_id TEXT NOT NULL REFERENCES stopwatches(id),
                    reset_utc INTEGER NOT NULL);
                """, transaction);
            Execute(connection, "CREATE INDEX idx_goal_resets_stopwatch ON goal_resets (stopwatch_id);", transaction);
            Execute(connection, "PRAGMA user_version = 14;", transaction);
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
