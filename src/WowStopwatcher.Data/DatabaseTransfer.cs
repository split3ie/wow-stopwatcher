using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace WowStopwatcher.Data;

/// <summary>
/// Copies, inspects, backs up, exports and imports database files. Databases are always copied with the SQLite online backup API
/// (consistent while the source is open in WAL mode) and the copy is checked with <c>PRAGMA integrity_check</c>.
/// Nothing here deletes or overwrites an existing database; a replaced file is renamed aside.
/// </summary>
public sealed class DatabaseTransfer
{
    /// <summary>Name of the database entry inside an export zip.</summary>
    public const string ZipDatabaseEntry = DatabaseLocation.DatabaseFileName;

    /// <summary>Name of the templates folder, in a data folder and inside an export zip.</summary>
    public const string TemplatesFolderName = "templates";

    private readonly TimeProvider _time;

    public DatabaseTransfer(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>
    /// Copies <paramref name="sourceDbPath"/> to <paramref name="targetDbPath"/> through <c>target.tmp</c>, verifies the copy, then renames it.
    /// Never overwrites an existing target. Works while the source is open.
    /// </summary>
    /// <exception cref="DatabaseTransferException">Source missing, target exists, copy or verification failed.</exception>
    public void CopyVerified(string sourceDbPath, string targetDbPath)
    {
        sourceDbPath = Path.GetFullPath(sourceDbPath);
        targetDbPath = Path.GetFullPath(targetDbPath);
        if (!File.Exists(sourceDbPath))
        {
            throw new DatabaseTransferException(TransferErrorCode.SourceMissing, $"The database '{sourceDbPath}' does not exist.");
        }

        if (File.Exists(targetDbPath))
        {
            throw new DatabaseTransferException(TransferErrorCode.TargetExists, $"The database '{targetDbPath}' already exists.");
        }

        var temp = targetDbPath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetDbPath)!);
            DeleteWithSidecars(temp);

            using (var source = new SqliteConnection(ConnectionString(sourceDbPath, SqliteOpenMode.ReadOnly)))
            using (var destination = new SqliteConnection(ConnectionString(temp, SqliteOpenMode.ReadWriteCreate)))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }

            using (var check = new SqliteConnection(ConnectionString(temp, SqliteOpenMode.ReadOnly)))
            {
                check.Open();
                var result = Scalar(check, "PRAGMA integrity_check;") as string;
                if (result != "ok")
                {
                    throw new InvalidDataException($"The copy failed the integrity check: {result}");
                }
            }

            File.Move(temp, targetDbPath);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            DeleteWithSidecarsQuietly(temp);
            if (File.Exists(targetDbPath) && ex is IOException)
            {
                throw new DatabaseTransferException(TransferErrorCode.TargetExists, $"The database '{targetDbPath}' already exists.", ex);
            }

            throw new DatabaseTransferException(TransferErrorCode.CopyFailed, $"The database could not be copied: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Copies <c>templates/</c> from one data folder to another, if present. A file that already exists with different content is left alone.
    /// Returns the number of files skipped for that reason.
    /// </summary>
    public int CopyTemplates(string sourceFolder, string targetFolder)
    {
        var source = Path.Combine(Path.GetFullPath(sourceFolder), TemplatesFolderName);
        var target = Path.Combine(Path.GetFullPath(targetFolder), TemplatesFolderName);
        if (!Directory.Exists(source) || string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var skipped = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (File.Exists(destination))
                {
                    if (!SameContent(file, destination))
                    {
                        skipped++;
                    }

                    continue;
                }

                File.Copy(file, destination);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DatabaseTransferException(TransferErrorCode.Io, $"The templates could not be copied: {ex.Message}", ex);
        }

        return skipped;
    }

    /// <summary>
    /// Summarizes a database with a read-only connection; never migrates or writes.
    /// A damaged file or a file that is not a database gives <see cref="DatabaseInspection.IntegrityOk"/> false.
    /// </summary>
    /// <exception cref="DatabaseTransferException">The file does not exist.</exception>
    /// <exception cref="DatabaseOpenException">The file could not be opened for another reason.</exception>
    public DatabaseInspection Inspect(string dbPath)
    {
        dbPath = Path.GetFullPath(dbPath);
        if (!File.Exists(dbPath))
        {
            throw new DatabaseTransferException(TransferErrorCode.SourceMissing, $"The database '{dbPath}' does not exist.");
        }

        try
        {
            using var connection = new SqliteConnection(ConnectionString(dbPath, SqliteOpenMode.ReadOnly));
            connection.Open();

            var version = Convert.ToInt32(Scalar(connection, "PRAGMA user_version;"));
            var newer = version > SqliteDatabase.LatestSchemaVersion;
            if (Scalar(connection, "PRAGMA integrity_check;") as string != "ok")
            {
                return new DatabaseInspection(version, newer, false, 0, 0, null);
            }

            if (version < 1)
            {
                return new DatabaseInspection(version, newer, true, 0, 0, null);
            }

            var stopwatches = Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM stopwatches WHERE is_deleted = 0;"));
            var days = Convert.ToInt32(Scalar(connection, "SELECT COUNT(DISTINCT date(start_utc / 1000, 'unixepoch', 'localtime')) FROM sessions;"));
            DateTimeOffset? last = Scalar(connection, "SELECT MAX(start_utc) FROM sessions;") is long ms
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                : null;
            return new DatabaseInspection(version, newer, true, stopwatches, days, last);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 11 or 26)
        {
            return new DatabaseInspection(0, false, false, 0, 0, null);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            throw new DatabaseOpenException(DatabaseProblem.Unavailable, dbPath, "The database file could not be opened.", ex);
        }
    }

    /// <summary>
    /// Backs up a database to <c>&lt;its folder&gt;/backups/&lt;name&gt;-before-&lt;reason&gt;-&lt;yyyyMMdd-HHmmss&gt;.db</c> with the online backup API
    /// and verifies the copy. These names are not matched by <see cref="DatabaseBackupCleaner"/>. Returns the backup path.
    /// </summary>
    /// <exception cref="DatabaseTransferException">The backup could not be made; no partial file is left.</exception>
    public string BackupExisting(string dbPath, string reason)
    {
        dbPath = Path.GetFullPath(dbPath);
        var folder = Path.Combine(Path.GetDirectoryName(dbPath)!, "backups");
        string? created = null;
        try
        {
            Directory.CreateDirectory(folder);
            var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var baseName = $"{Path.GetFileNameWithoutExtension(dbPath)}-before-{reason}-{stamp}";
            var target = Path.Combine(folder, baseName + ".db");
            for (var n = 2; File.Exists(target); n++)
            {
                target = Path.Combine(folder, $"{baseName}-{n}.db");
            }

            created = target;
            CopyVerified(dbPath, target);
            return target;
        }
        catch (DatabaseTransferException ex)
        {
            throw new DatabaseTransferException(TransferErrorCode.BackupFailed, $"The backup could not be made: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (created is not null)
            {
                DeleteWithSidecarsQuietly(created);
            }

            throw new DatabaseTransferException(TransferErrorCode.BackupFailed, $"The backup could not be made: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Writes <paramref name="zipPath"/> with a consistent, verified copy of the database (<c>wowstopwatcher.db</c>) and the
    /// <c>templates</c> folder inside <paramref name="dataFolder"/> (if present). Written to <c>.tmp</c> first, then renamed over the target.
    /// </summary>
    public void Export(string sourceDbPath, string dataFolder, string zipPath)
    {
        zipPath = Path.GetFullPath(zipPath);
        var temp = NewTempFolder();
        var tempZip = zipPath + ".tmp";
        try
        {
            var dbCopy = Path.Combine(temp, ZipDatabaseEntry);
            CopyVerified(sourceDbPath, dbCopy);

            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
            File.Delete(tempZip);
            using (var archive = ZipFile.Open(tempZip, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(dbCopy, ZipDatabaseEntry, CompressionLevel.Optimal);
                var templates = Path.Combine(Path.GetFullPath(dataFolder), TemplatesFolderName);
                if (Directory.Exists(templates))
                {
                    foreach (var file in Directory.EnumerateFiles(templates, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(templates, file).Replace('\\', '/');
                        archive.CreateEntryFromFile(file, $"{TemplatesFolderName}/{relative}", CompressionLevel.Optimal);
                    }
                }
            }

            File.Move(tempZip, zipPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            TryDelete(tempZip);
            throw new DatabaseTransferException(TransferErrorCode.Io, $"The backup file could not be written: {ex.Message}", ex);
        }
        catch (DatabaseTransferException)
        {
            TryDelete(tempZip);
            throw;
        }
        finally
        {
            TryDeleteFolder(temp);
        }
    }

    /// <summary>
    /// Checks a backup zip and extracts it to a temp folder: it must be a zip, contain <c>wowstopwatcher.db</c>, pass the integrity check
    /// and not have a newer schema. Dispose the result to delete the temp folder.
    /// </summary>
    public ImportValidation ValidateImport(string zipPath)
    {
        var temp = NewTempFolder();
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var dbEntry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, ZipDatabaseEntry, StringComparison.OrdinalIgnoreCase));
            if (dbEntry is null)
            {
                return Fail(temp, TransferErrorCode.NoDatabase);
            }

            var dbPath = Path.Combine(temp, ZipDatabaseEntry);
            dbEntry.ExtractToFile(dbPath);

            var prefix = TemplatesFolderName + "/";
            var templatesRoot = Path.Combine(temp, TemplatesFolderName);
            foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !e.FullName.EndsWith('/')))
            {
                var destination = Path.GetFullPath(Path.Combine(temp, entry.FullName));
                if (!destination.StartsWith(templatesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }

            var inspection = Inspect(dbPath);
            if (!inspection.IntegrityOk)
            {
                return Fail(temp, TransferErrorCode.Damaged, inspection);
            }

            if (inspection.IsNewerThanApp)
            {
                return Fail(temp, TransferErrorCode.NewerVersion, inspection);
            }

            return new ImportValidation(temp, dbPath, templatesRoot, inspection, null);
        }
        catch (InvalidDataException)
        {
            return Fail(temp, TransferErrorCode.NotAZip);
        }
        catch (FileNotFoundException)
        {
            return Fail(temp, TransferErrorCode.SourceMissing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DatabaseOpenException)
        {
            return Fail(temp, TransferErrorCode.Io);
        }
    }

    /// <summary>
    /// Runs the pending step of <c>location.json</c>, if any. Call it at startup before anything opens the database.
    /// The pending step is always cleared afterwards. On failure nothing switches and the current database is unchanged.
    /// Returns null when nothing was pending (or <c>location.json</c> is damaged: resolve that first).
    /// </summary>
    public PendingOutcome? ApplyPending(DatabaseLocation location)
    {
        var read = location.Read();
        if (read.IsDamaged || read.Settings.Pending is not { } step)
        {
            return null;
        }

        PendingOutcome outcome;
        try
        {
            switch (step.Kind)
            {
                case PendingKind.Move:
                    ApplyMove(location, step);
                    break;
                case PendingKind.Replace:
                    ApplyReplace(location, step);
                    break;
                case PendingKind.Import:
                    ApplyImport(step);
                    break;
                default:
                    throw new DatabaseTransferException(TransferErrorCode.Io, $"Unknown pending step '{step.Kind}'.");
            }

            outcome = new PendingOutcome(step.Kind, null, null);
        }
        catch (DatabaseTransferException ex)
        {
            outcome = new PendingOutcome(step.Kind, ex.Code, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or DatabaseOpenException)
        {
            outcome = new PendingOutcome(step.Kind, TransferErrorCode.Io, ex.Message);
        }

        try
        {
            location.ClearPending();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (outcome.Success)
            {
                outcome = new PendingOutcome(step.Kind, null, $"The pending step could not be cleared: {ex.Message}");
            }
        }

        return outcome;
    }

    private void ApplyMove(DatabaseLocation location, PendingStep step)
    {
        CopyVerified(step.Source, step.Target);
        CopyTemplates(Path.GetDirectoryName(Path.GetFullPath(step.Source))!, Path.GetDirectoryName(Path.GetFullPath(step.Target))!);
        location.SetFolder(Path.GetDirectoryName(Path.GetFullPath(step.Target)));
    }

    private void ApplyReplace(DatabaseLocation location, PendingStep step)
    {
        var target = Path.GetFullPath(step.Target);
        var incoming = target + ".incoming";
        try
        {
            DeleteWithSidecars(incoming);
            CopyVerified(step.Source, incoming);
            if (File.Exists(target))
            {
                BackupExisting(target, "replace");
            }

            SwapIn(incoming, target);
        }
        finally
        {
            DeleteWithSidecarsQuietly(incoming);
        }

        CopyTemplates(Path.GetDirectoryName(Path.GetFullPath(step.Source))!, Path.GetDirectoryName(target)!);
        location.SetFolder(Path.GetDirectoryName(target));
    }

    private void ApplyImport(PendingStep step)
    {
        using var validation = ValidateImport(step.Source);
        if (!validation.IsValid)
        {
            throw new DatabaseTransferException(validation.Error!.Value, $"The backup file '{step.Source}' cannot be imported ({validation.Error}).");
        }

        var target = Path.GetFullPath(step.Target);
        var incoming = target + ".incoming";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            DeleteWithSidecars(incoming);
            CopyVerified(validation.DatabasePath!, incoming);
            if (File.Exists(target))
            {
                BackupExisting(target, "import");
            }

            SwapIn(incoming, target);
        }
        finally
        {
            DeleteWithSidecarsQuietly(incoming);
        }

        CopyTemplates(Path.GetDirectoryName(validation.DatabasePath!)!, Path.GetDirectoryName(target)!);
    }

    /// <summary>
    /// Puts <paramref name="incoming"/> at <paramref name="target"/>. An existing target (with its -wal and -shm files) is renamed
    /// aside to <c>.replaced-&lt;timestamp&gt;</c> first, and restored if the move fails. It is never deleted.
    /// </summary>
    private void SwapIn(string incoming, string target)
    {
        string[] suffixes = ["", "-wal", "-shm"];
        var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var moved = new List<(string From, string To)>();
        try
        {
            if (File.Exists(target))
            {
                var aside = $"{target}.replaced-{stamp}";
                for (var n = 2; File.Exists(aside); n++)
                {
                    aside = $"{target}.replaced-{stamp}-{n}";
                }

                foreach (var suffix in suffixes)
                {
                    if (File.Exists(target + suffix))
                    {
                        File.Move(target + suffix, aside + suffix);
                        moved.Add((target + suffix, aside + suffix));
                    }
                }
            }

            File.Move(incoming, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var (from, to) in moved)
            {
                try
                {
                    File.Move(to, from);
                }
                catch (Exception restore) when (restore is IOException or UnauthorizedAccessException)
                {
                    // Best effort: the renamed file stays next to the target.
                }
            }

            throw new DatabaseTransferException(TransferErrorCode.Io, $"The database could not be replaced: {ex.Message}", ex);
        }
    }

    private static ImportValidation Fail(string temp, TransferErrorCode error, DatabaseInspection? inspection = null)
    {
        TryDeleteFolder(temp);
        return new ImportValidation(null, null, null, inspection, error);
    }

    private static string NewTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-transfer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static bool SameContent(string a, string b) =>
        new FileInfo(a).Length == new FileInfo(b).Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    private static string ConnectionString(string file, SqliteOpenMode mode) => new SqliteConnectionStringBuilder
    {
        DataSource = file,
        Mode = mode,
        Pooling = false,
    }.ToString();

    private static void DeleteWithSidecars(string path)
    {
        File.Delete(path);
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
        File.Delete(path + "-journal");
    }

    private static void DeleteWithSidecarsQuietly(string path)
    {
        try
        {
            DeleteWithSidecars(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover temp file of ours is replaced next time.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover temp file of ours is replaced next time.
        }
    }

    private static void TryDeleteFolder(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the OS cleans the temp folder eventually.
        }
    }
}
