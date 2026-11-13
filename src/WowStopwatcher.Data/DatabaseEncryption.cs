using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using WowStopwatcher.Core.Security;

namespace WowStopwatcher.Data;

/// <summary>
/// Encrypts an existing plain database in place. Call it at startup, before anything opens the database, and call
/// <see cref="Recover"/> first so a leftover of an interrupted run is finished or undone.
/// <para>
/// <see cref="Encrypt"/> steps (D = database, T = <c>D.encrypting</c>, A = <c>D.plain-aside</c>, K = the key file, B = the plain backup
/// <c>backups/&lt;name&gt;-before-encryption.db</c>):
/// 1. write K; 2. make and verify B; 3. checkpoint D, copy it plain to T, <c>PRAGMA rekey</c> T, reopen T with the key and compare
/// <c>integrity_check</c> and <c>user_version</c>; 4. move D to A; 5. move T to D; 6. delete the plain schema backups and B, then A.
/// </para>
/// <para>
/// States on disk, and what <see cref="Recover"/> does:
/// D plain (with or without T, B, K): before step 4, so T and B are deleted, K too (<see cref="EncryptionRecoveryResult.RolledBack"/>).
/// D missing, A and a verified T (and K): between steps 4 and 5, so T is moved to D and the cleanup finishes (Completed).
/// D missing, A without a usable T: A is moved back to D (RolledBack).
/// D encrypted and A present: step 6 was interrupted; once D opens with the key, the leftovers are deleted, A last (Completed).
/// D plain and A present, or D missing with T but no A: not produced by this class, nothing is deleted (Conflict).
/// At every point D is the original plain database or the verified encrypted one, or (briefly, between steps 4 and 5) missing with
/// the complete plain copy in A.
/// </para>
/// </summary>
public sealed class DatabaseEncryption
{
    /// <summary>Suffix of the encrypted copy while it is being built.</summary>
    public const string TempSuffix = ".encrypting";

    /// <summary>Suffix of the plain database while it is being replaced.</summary>
    public const string AsideSuffix = ".plain-aside";

    private readonly string _databasePath;
    private readonly string _tempPath;
    private readonly string _asidePath;
    private readonly string _backupFolder;
    private readonly string _name;

    public DatabaseEncryption(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
        _tempPath = _databasePath + TempSuffix;
        _asidePath = _databasePath + AsideSuffix;
        _backupFolder = Path.Combine(Path.GetDirectoryName(_databasePath)!, "backups");
        _name = Path.GetFileNameWithoutExtension(_databasePath);
    }

    /// <summary>
    /// Encrypts the plain database with <paramref name="key"/> and stores <paramref name="keyFile"/> (the key wrapped by the recovery code)
    /// next to it. Throws <see cref="DatabaseEncryptionException"/> on failure; the original plain database is then still at the path.
    /// </summary>
    public void Encrypt(DatabaseKey key, DatabaseKeyFileContents keyFile)
    {
        DatabaseFileFormat format;
        try
        {
            format = DatabaseFormatDetector.Detect(_databasePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DatabaseEncryptionException("The database file could not be read.", ex);
        }

        if (format != DatabaseFileFormat.Plain)
        {
            throw new DatabaseEncryptionException($"Only a plain database can be encrypted (found: {format}).");
        }

        if (File.Exists(_asidePath))
        {
            throw new DatabaseEncryptionException("An earlier encryption left a file behind; run Recover first.");
        }

        string? backup = null;
        try
        {
            DeleteWithSidecars(_tempPath);

            // The key file comes first: no encrypted database exists at the path without it. D is plain here, so an older file is stale.
            DatabaseKeyFile.Write(_databasePath, keyFile, overwrite: true);

            backup = BackupPlain();
            var version = CheckpointPlain();
            BuildEncryptedCopy(key, version);
        }
        catch (Exception ex)
        {
            Undo(backup);
            throw new DatabaseEncryptionException("The database could not be encrypted; it was left unchanged.", ex);
        }

        try
        {
            File.Move(_databasePath, _asidePath);
        }
        catch (Exception ex)
        {
            Undo(backup);
            throw new DatabaseEncryptionException("The database could not be encrypted; it was left unchanged.", ex);
        }

        try
        {
            DeleteIfExists(_databasePath + "-wal");
            DeleteIfExists(_databasePath + "-shm");
            File.Move(_tempPath, _databasePath);
        }
        catch (Exception ex)
        {
            try
            {
                File.Move(_asidePath, _databasePath, overwrite: true);
            }
            catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException)
            {
                throw new DatabaseEncryptionException("The database could not be encrypted and the original is in its .plain-aside file; run Recover.", ex);
            }

            Undo(backup);
            throw new DatabaseEncryptionException("The database could not be encrypted; it was left unchanged.", ex);
        }

        TryCleanup();
    }

    /// <summary>
    /// Finishes or undoes an interrupted <see cref="Encrypt"/> (see the states in the class description). <paramref name="key"/> is needed to
    /// verify an encrypted file; without it only rollbacks are possible.
    /// </summary>
    public EncryptionRecoveryResult Recover(DatabaseKey? key)
    {
        try
        {
            var format = DatabaseFormatDetector.Detect(_databasePath);
            var hasTemp = File.Exists(_tempPath);
            var hasAside = File.Exists(_asidePath);
            var hasBackup = FindBackups().Count > 0;

            switch (format)
            {
                case DatabaseFileFormat.Missing when hasAside:
                    if (hasTemp && key is not null && File.Exists(DatabaseKeyFile.PathFor(_databasePath)) && VerifyTemp(key))
                    {
                        DeleteIfExists(_databasePath + "-wal");
                        DeleteIfExists(_databasePath + "-shm");
                        File.Move(_tempPath, _databasePath, overwrite: true);
                        TryCleanup();
                        return EncryptionRecoveryResult.Completed;
                    }

                    File.Move(_asidePath, _databasePath, overwrite: true);
                    Undo(null);
                    return EncryptionRecoveryResult.RolledBack;

                case DatabaseFileFormat.Plain when hasAside:
                case DatabaseFileFormat.Missing when hasTemp:
                    return EncryptionRecoveryResult.Conflict;

                case DatabaseFileFormat.Plain when hasTemp || hasBackup:
                    Undo(null);
                    return EncryptionRecoveryResult.RolledBack;

                case DatabaseFileFormat.Encrypted when hasAside:
                    if (key is null || !Opens(_databasePath, key))
                    {
                        return EncryptionRecoveryResult.Conflict;
                    }

                    DeleteWithSidecars(_tempPath);
                    TryCleanup();
                    return EncryptionRecoveryResult.Completed;

                case DatabaseFileFormat.Encrypted when hasTemp || hasBackup:
                    DeleteWithSidecars(_tempPath);
                    DeleteBackups();
                    return EncryptionRecoveryResult.Completed;

                default:
                    return EncryptionRecoveryResult.NothingToDo;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            throw new DatabaseEncryptionException("An interrupted encryption could not be recovered.", ex);
        }
    }

    /// <summary>Copies D to <c>backups/&lt;name&gt;-before-encryption.db</c> (never overwriting) and verifies the copy.</summary>
    private string BackupPlain()
    {
        Directory.CreateDirectory(_backupFolder);
        var baseName = $"{_name}-before-encryption";
        var target = Path.Combine(_backupFolder, baseName + ".db");
        for (var n = 2; File.Exists(target); n++)
        {
            target = Path.Combine(_backupFolder, $"{baseName}-{n}.db");
        }

        int version;
        using (var source = DatabaseConnections.Open(_databasePath, SqliteOpenMode.ReadOnly, null))
        using (var destination = DatabaseConnections.Open(target, SqliteOpenMode.ReadWriteCreate, null))
        {
            version = DatabaseConnections.ReadVersion(source);
            source.BackupDatabase(destination);
        }

        using var check = DatabaseConnections.Open(target, SqliteOpenMode.ReadOnly, null);
        if (DatabaseConnections.ReadVersion(check) != version)
        {
            throw new InvalidDataException("The backup has a different schema version.");
        }

        var result = DatabaseConnections.Scalar(check, "PRAGMA quick_check;") as string;
        if (result != "ok")
        {
            throw new InvalidDataException($"Backup integrity check failed: {result}");
        }

        return target;
    }

    /// <summary>Moves everything in D's write-ahead log into D, so D alone holds the data. Returns its schema version.</summary>
    private int CheckpointPlain()
    {
        int version;
        using (var connection = DatabaseConnections.Open(_databasePath, SqliteOpenMode.ReadWrite, null))
        {
            version = DatabaseConnections.ReadVersion(connection);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            using var reader = command.ExecuteReader();
            if (reader.Read() && reader.GetInt32(0) != 0)
            {
                throw new InvalidDataException("The database is in use and could not be checkpointed.");
            }
        }

        var wal = _databasePath + "-wal";
        if (File.Exists(wal) && new FileInfo(wal).Length > 0)
        {
            throw new InvalidDataException("The write-ahead log of the database is not empty.");
        }

        DeleteIfExists(wal);
        DeleteIfExists(_databasePath + "-shm");
        return version;
    }

    /// <summary>Copies D plain into T, encrypts T with <paramref name="key"/> and checks it.</summary>
    private void BuildEncryptedCopy(DatabaseKey key, int version)
    {
        using (var source = DatabaseConnections.Open(_databasePath, SqliteOpenMode.ReadOnly, null))
        using (var destination = DatabaseConnections.Open(_tempPath, SqliteOpenMode.ReadWriteCreate, null))
        {
            source.BackupDatabase(destination);

            // Rekey needs a rollback journal, and a single file without a -wal keeps the swap simple. SqliteDatabase switches to WAL again.
            var mode = DatabaseConnections.Scalar(destination, "PRAGMA journal_mode = DELETE;") as string;
            if (!string.Equals(mode, "delete", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"The copy could not leave WAL mode (journal mode: {mode}).");
            }

            DatabaseConnections.Rekey(destination, key);
        }

        if (DatabaseFormatDetector.Detect(_tempPath) != DatabaseFileFormat.Encrypted)
        {
            throw new InvalidDataException("The copy was not encrypted.");
        }

        using var check = DatabaseConnections.Open(_tempPath, SqliteOpenMode.ReadOnly, key);
        if (DatabaseConnections.Scalar(check, "PRAGMA integrity_check;") as string != "ok")
        {
            throw new InvalidDataException("The encrypted copy failed its integrity check.");
        }

        if (DatabaseConnections.ReadVersion(check) != version)
        {
            throw new InvalidDataException("The encrypted copy has a different schema version.");
        }
    }

    /// <summary>True when T is a complete encrypted copy of A: opens with the key, passes integrity_check and has A's schema version.</summary>
    private bool VerifyTemp(DatabaseKey key)
    {
        try
        {
            if (DatabaseFormatDetector.Detect(_tempPath) != DatabaseFileFormat.Encrypted
                || DatabaseFormatDetector.Detect(_asidePath) != DatabaseFileFormat.Plain)
            {
                return false;
            }

            int expected;
            using (var plain = DatabaseConnections.Open(_asidePath, SqliteOpenMode.ReadOnly, null))
            {
                expected = DatabaseConnections.ReadVersion(plain);
            }

            using var check = DatabaseConnections.Open(_tempPath, SqliteOpenMode.ReadOnly, key);
            return DatabaseConnections.Scalar(check, "PRAGMA integrity_check;") as string == "ok"
                && DatabaseConnections.ReadVersion(check) == expected;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>True when the encrypted database at <paramref name="path"/> opens with <paramref name="key"/> (a light read-only check, no integrity scan).</summary>
    public static bool KeyOpens(string path, DatabaseKey key) => Opens(path, key);

    private static bool Opens(string path, DatabaseKey key)
    {
        try
        {
            using var connection = DatabaseConnections.Open(path, SqliteOpenMode.ReadOnly, key);
            DatabaseConnections.ReadVersion(connection);
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary>Removes the plaintext leftovers after D is encrypted: B, the plain schema backups, then A (the marker, so it goes last).</summary>
    private void TryCleanup()
    {
        try
        {
            DeleteBackups();
            DeletePlainSchemaBackups();
            DeleteWithSidecars(_asidePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: A stays as the marker, so the next Recover retries.
        }
    }

    /// <summary>Removes what an encryption that did not finish created while D is still the plain original: T, B and K.</summary>
    private void Undo(string? backup)
    {
        try
        {
            DeleteWithSidecars(_tempPath);
            if (backup is not null)
            {
                DeleteWithSidecars(backup);
            }

            DeleteBackups();
            DeleteIfExists(DatabaseKeyFile.PathFor(_databasePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the leftovers are found and removed by the next Recover.
        }
    }

    private List<string> FindBackups() => FindFiles($@"^{Regex.Escape(_name)}-before-encryption(-\d+)?\.db$");

    private void DeleteBackups()
    {
        foreach (var file in FindBackups())
        {
            DeleteWithSidecars(file);
        }
    }

    /// <summary>Deletes <c>backups/&lt;name&gt;-v&lt;N&gt;.db</c> and <c>-v&lt;N&gt;-&lt;k&gt;.db</c> that are plain; encrypted ones are kept.</summary>
    private void DeletePlainSchemaBackups()
    {
        foreach (var file in FindFiles($@"^{Regex.Escape(_name)}-v\d+(-\d+)?\.db$"))
        {
            if (DatabaseFormatDetector.Detect(file) == DatabaseFileFormat.Plain)
            {
                DeleteWithSidecars(file);
            }
        }
    }

    private List<string> FindFiles(string pattern)
    {
        if (!Directory.Exists(_backupFolder))
        {
            return [];
        }

        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Directory.EnumerateFiles(_backupFolder).Where(f => regex.IsMatch(Path.GetFileName(f))).ToList();
    }

    private static void DeleteWithSidecars(string path)
    {
        DeleteIfExists(path + "-wal");
        DeleteIfExists(path + "-shm");
        DeleteIfExists(path + "-journal");
        DeleteIfExists(path);
    }

    private static void DeleteIfExists(string path) => File.Delete(path);
}
