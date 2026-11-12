using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using WowStopwatcher.Core.Security;

namespace WowStopwatcher.Data;

/// <summary>
/// Copies, inspects, backs up, exports and imports database files. Databases are always copied with the SQLite online backup API
/// (consistent while the source is open in WAL mode) and the copy is checked with <c>PRAGMA integrity_check</c>.
/// Nothing here deletes or overwrites an existing database; a replaced file is renamed aside.
/// <para>
/// Encrypted databases: the caller passes the <see cref="DatabaseKey"/> as a parameter; this class never stores a key (only the key file
/// and the export envelope contain it). Every copy of an encrypted database is made with the backup API under the same key and gets the
/// source's key file next to it (<see cref="DatabaseKeyFile.PathFor"/>), never overwriting an existing one. When the key is not available
/// (for example the existing database in a target folder that is about to be replaced) the closed file is byte-copied with its key file
/// instead, verified by length and SHA-256 (<see cref="CopyClosedVerified"/>); that needs no key but cannot run an integrity check
/// and refuses a database that still has a non-empty <c>-wal</c> file. A database and its key file are always placed together.
/// </para>
/// </summary>
public sealed class DatabaseTransfer
{
    /// <summary>Name of the database entry inside an export zip.</summary>
    public const string ZipDatabaseEntry = DatabaseLocation.DatabaseFileName;

    /// <summary>Name of the key file entry inside a <c>.wsbackup</c> (the key file of <see cref="ZipDatabaseEntry"/>).</summary>
    public const string ZipKeyFileEntry = "wowstopwatcher.key.json";

    /// <summary>Name of the entry inside a <c>.wsbackup</c> that holds the raw database key as 64 hex digits.</summary>
    public const string ZipKeyEntry = "database.key";

    /// <summary>File extension of a password-protected backup (see <see cref="BackupEnvelope"/>), with the dot.</summary>
    public const string BackupExtension = ".wsbackup";

    /// <summary>Folder inside a data folder that holds staged imports (<see cref="StageImport"/>), one subfolder per import.</summary>
    public const string StagingFolderName = "import-staging";

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
    /// An encrypted source is copied with <paramref name="key"/> (same key, integrity checked) and its key file is copied next to the target;
    /// without a key the closed file is byte-copied instead (see <see cref="CopyClosedVerified"/>).
    /// </summary>
    /// <exception cref="DatabaseTransferException">Source missing, target (or its key file) exists, the key is rejected, copy or verification failed.</exception>
    public void CopyVerified(string sourceDbPath, string targetDbPath, DatabaseKey? key = null)
    {
        sourceDbPath = Path.GetFullPath(sourceDbPath);
        targetDbPath = Path.GetFullPath(targetDbPath);
        CheckCopyPaths(sourceDbPath, targetDbPath);

        var encrypted = IsEncrypted(sourceDbPath, key);
        if (encrypted && key is null)
        {
            CopyClosedVerified(sourceDbPath, targetDbPath);
            return;
        }

        var useKey = encrypted ? key : null;
        var temp = targetDbPath + ".tmp";
        var sourceKeyFile = DatabaseKeyFile.PathFor(sourceDbPath);
        var targetKeyFile = DatabaseKeyFile.PathFor(targetDbPath);
        var keyFilePlaced = false;
        try
        {
            StageVerified(sourceDbPath, temp, useKey);
            if (encrypted && File.Exists(sourceKeyFile))
            {
                CopyFileVerified(sourceKeyFile, targetKeyFile + ".tmp");
                File.Move(targetKeyFile + ".tmp", targetKeyFile);
                keyFilePlaced = true;
            }

            File.Move(temp, targetDbPath);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            DeleteWithSidecarsQuietly(temp);
            TryDelete(targetKeyFile + ".tmp");
            if (keyFilePlaced)
            {
                TryDelete(targetKeyFile);
            }

            throw Translate(ex, targetDbPath, useKey);
        }
    }

    /// <summary>
    /// Copies a closed database file and its key file byte for byte, for an encrypted database whose key the caller does not have.
    /// Each file goes through a <c>.tmp</c> file and is verified by length and SHA-256 before it is renamed. Never overwrites a database
    /// or key file. The source must not be open: a non-empty <c>-wal</c> file (uncommitted data a byte copy would miss) is refused.
    /// There is no integrity check (it needs the key).
    /// </summary>
    /// <exception cref="DatabaseTransferException">Source missing, target (or its key file) exists, the source has a pending <c>-wal</c> file, or the copy failed.</exception>
    public void CopyClosedVerified(string sourceDbPath, string targetDbPath)
    {
        sourceDbPath = Path.GetFullPath(sourceDbPath);
        targetDbPath = Path.GetFullPath(targetDbPath);
        CheckCopyPaths(sourceDbPath, targetDbPath);

        var sourceKeyFile = DatabaseKeyFile.PathFor(sourceDbPath);
        var targetKeyFile = DatabaseKeyFile.PathFor(targetDbPath);
        var temp = targetDbPath + ".tmp";
        var keyFilePlaced = false;
        try
        {
            RequireNoPendingWal(sourceDbPath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetDbPath)!);
            DeleteWithSidecars(temp);
            CopyFileVerified(sourceDbPath, temp);
            if (File.Exists(sourceKeyFile))
            {
                CopyFileVerified(sourceKeyFile, targetKeyFile + ".tmp");
                File.Move(targetKeyFile + ".tmp", targetKeyFile);
                keyFilePlaced = true;
            }

            File.Move(temp, targetDbPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            TryDelete(temp);
            TryDelete(targetKeyFile + ".tmp");
            if (keyFilePlaced)
            {
                TryDelete(targetKeyFile);
            }

            throw Translate(ex, targetDbPath, null);
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
    /// An encrypted database is opened with <paramref name="key"/>; without a key, or with a key that does not open it, the result has
    /// <see cref="DatabaseInspection.KeyProblem"/> set to <see cref="TransferErrorCode.KeyRequired"/> or <see cref="TransferErrorCode.KeyRejected"/>
    /// so the caller can ask for that database's recovery code. A plain database ignores <paramref name="key"/>.
    /// </summary>
    /// <exception cref="DatabaseTransferException">The file does not exist.</exception>
    /// <exception cref="DatabaseOpenException">The file could not be opened for another reason.</exception>
    public DatabaseInspection Inspect(string dbPath, DatabaseKey? key = null)
    {
        dbPath = Path.GetFullPath(dbPath);
        if (!File.Exists(dbPath))
        {
            throw new DatabaseTransferException(TransferErrorCode.SourceMissing, $"The database '{dbPath}' does not exist.");
        }

        bool encrypted;
        try
        {
            encrypted = IsEncrypted(dbPath, key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DatabaseOpenException(DatabaseProblem.Unavailable, dbPath, "The database file could not be opened.", ex);
        }

        if (encrypted && key is null)
        {
            return new DatabaseInspection(0, false, false, 0, 0, null, TransferErrorCode.KeyRequired);
        }

        try
        {
            using var connection = DatabaseConnections.Open(dbPath, SqliteOpenMode.ReadOnly, encrypted ? key : null);

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
        catch (SqliteException ex) when (ex.SqliteErrorCode == 26 && encrypted)
        {
            return new DatabaseInspection(0, false, false, 0, 0, null, TransferErrorCode.KeyRejected);
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
    /// An encrypted database is backed up with <paramref name="key"/> (or byte-copied without one, see <see cref="CopyVerified"/>);
    /// its key file is copied next to the backup.
    /// </summary>
    /// <exception cref="DatabaseTransferException">The backup could not be made; no partial file is left.</exception>
    public string BackupExisting(string dbPath, string reason, DatabaseKey? key = null)
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
            CopyVerified(dbPath, target, key);
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
    /// Writes <paramref name="zipPath"/> with a consistent, verified copy of the plain database (<c>wowstopwatcher.db</c>) and the
    /// <c>templates</c> folder inside <paramref name="dataFolder"/> (if present). Written to <c>.tmp</c> first, then renamed over the target.
    /// This is the old, unprotected format; an encrypted database is exported with <see cref="ExportEncrypted"/>.
    /// </summary>
    /// <exception cref="DatabaseTransferException">The database is encrypted (<see cref="TransferErrorCode.KeyRequired"/>) or the file could not be written.</exception>
    public void Export(string sourceDbPath, string dataFolder, string zipPath)
    {
        zipPath = Path.GetFullPath(zipPath);
        if (IsEncrypted(Path.GetFullPath(sourceDbPath), null))
        {
            throw new DatabaseTransferException(TransferErrorCode.KeyRequired, "An encrypted database must be exported with a key and a password.");
        }

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
                AddDatabaseAndTemplates(archive, dbCopy, dataFolder);
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
    /// Writes a password-protected backup (<see cref="BackupExtension"/>, <see cref="BackupEnvelope"/>) of an encrypted database: a zip with the
    /// database (copied with the backup API under <paramref name="key"/>), its key file (<see cref="ZipKeyFileEntry"/>), the raw key as hex
    /// (<see cref="ZipKeyEntry"/>) and the <c>templates</c> folder, encrypted with AES-256-GCM under a key derived from
    /// <paramref name="password"/>. The whole zip is built and encrypted in memory, so backups are assumed to stay well below the free memory
    /// (a few hundred MB). Written to <c>.tmp</c> first, then renamed over the target.
    /// </summary>
    /// <exception cref="ArgumentException">The password is empty or shorter than <see cref="BackupEnvelope.MinPasswordLength"/>.</exception>
    /// <exception cref="DatabaseTransferException">The database is not encrypted or has no key file, the key is rejected, or the file could not be written.</exception>
    public void ExportEncrypted(string sourceDbPath, string dataFolder, string backupPath, DatabaseKey key, string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (!BackupEnvelope.IsAcceptablePassword(password))
        {
            throw new ArgumentException($"The password must be at least {BackupEnvelope.MinPasswordLength} characters.", nameof(password));
        }

        sourceDbPath = Path.GetFullPath(sourceDbPath);
        backupPath = Path.GetFullPath(backupPath);
        if (!File.Exists(sourceDbPath))
        {
            throw new DatabaseTransferException(TransferErrorCode.SourceMissing, $"The database '{sourceDbPath}' does not exist.");
        }

        if (!IsEncrypted(sourceDbPath, key))
        {
            throw new DatabaseTransferException(TransferErrorCode.CopyFailed, "Only an encrypted database can be exported with a password.");
        }

        if (!File.Exists(DatabaseKeyFile.PathFor(sourceDbPath)))
        {
            throw new DatabaseTransferException(TransferErrorCode.KeyFileMissing, "The database has no key file next to it.");
        }

        var temp = NewTempFolder();
        var tempFile = backupPath + ".tmp";
        try
        {
            var dbCopy = Path.Combine(temp, ZipDatabaseEntry);
            CopyVerified(sourceDbPath, dbCopy, key);

            byte[] zipBytes;
            using (var memory = new MemoryStream())
            {
                using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
                {
                    AddDatabaseAndTemplates(archive, dbCopy, dataFolder);
                    archive.CreateEntryFromFile(DatabaseKeyFile.PathFor(dbCopy), ZipKeyFileEntry, CompressionLevel.Optimal);
                    var keyEntry = archive.CreateEntry(ZipKeyEntry, CompressionLevel.Optimal);
                    using var writer = new StreamWriter(keyEntry.Open(), new UTF8Encoding(false));
                    writer.Write(key.ToHex());
                }

                zipBytes = memory.ToArray();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.WriteAllBytes(tempFile, BackupEnvelope.Encrypt(zipBytes, password));
            File.Move(tempFile, backupPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OutOfMemoryException)
        {
            TryDelete(tempFile);
            throw new DatabaseTransferException(TransferErrorCode.Io, $"The backup file could not be written: {ex.Message}", ex);
        }
        catch (DatabaseTransferException)
        {
            TryDelete(tempFile);
            throw;
        }
        finally
        {
            TryDeleteFolder(temp);
        }
    }

    /// <summary>
    /// Checks a backup file and extracts it to a temp folder. The kind is detected from the first bytes: a <see cref="BackupExtension"/> file
    /// (magic <c>WSBK</c>) needs <paramref name="password"/>; anything else is read as a plain zip (the old export format, whose database is plain).
    /// The database must be present, pass the integrity check and not have a newer schema. For a <c>.wsbackup</c> the result exposes
    /// <see cref="ImportValidation.Key"/> and <see cref="ImportValidation.KeyFile"/>. Errors: <see cref="TransferErrorCode.PasswordRequired"/>,
    /// <see cref="TransferErrorCode.WrongPassword"/> (also for a changed file: the two cannot be told apart) and <see cref="TransferErrorCode.Damaged"/>
    /// for a malformed envelope or content. A plain zip is only ever expected to hold a plain database; one holding an encrypted file is reported as damaged.
    /// Dispose the result to delete the temp folder.
    /// </summary>
    public ImportValidation ValidateImport(string path, string? password = null)
    {
        var temp = NewTempFolder();
        try
        {
            if (HasEnvelopeMagic(path))
            {
                return ValidateEnvelope(temp, path, password);
            }

            using var archive = ZipFile.OpenRead(path);
            var dbPath = ExtractArchive(archive, temp, withKeyFile: false);
            return dbPath is null ? Fail(temp, TransferErrorCode.NoDatabase) : Finish(temp, dbPath, null, null);
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
    /// Copies a validated <c>.wsbackup</c> into <c>&lt;dataFolder&gt;/import-staging/&lt;timestamp&gt;/</c>: the encrypted database, its key file
    /// and <c>templates/</c>. The raw key is never written to disk (the database is encrypted and the key file wraps the key with the recovery
    /// code); the App takes it from <see cref="ImportValidation.Key"/>. Point a <see cref="PendingKind.Import"/> step at the returned folder:
    /// <see cref="ApplyPending"/> then needs no password and deletes the folder afterwards. Call <see cref="DiscardStaging"/> if the import is
    /// cancelled or replaced.
    /// </summary>
    /// <exception cref="ArgumentException">The validation is not valid or is not from a <c>.wsbackup</c>.</exception>
    /// <exception cref="DatabaseTransferException">The files could not be copied; no partial folder is left.</exception>
    public string StageImport(ImportValidation validation, string dataFolder)
    {
        if (!validation.IsValid || validation.KeyFile is null || validation.DatabasePath is null)
        {
            throw new ArgumentException("Only a valid .wsbackup validation can be staged.", nameof(validation));
        }

        var root = Path.Combine(Path.GetFullPath(dataFolder), StagingFolderName);
        var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var folder = Path.Combine(root, stamp);
        for (var n = 2; Directory.Exists(folder); n++)
        {
            folder = Path.Combine(root, $"{stamp}-{n}");
        }

        try
        {
            Directory.CreateDirectory(folder);
            var db = Path.Combine(folder, ZipDatabaseEntry);
            CopyFileVerified(validation.DatabasePath, db);
            CopyFileVerified(DatabaseKeyFile.PathFor(validation.DatabasePath), DatabaseKeyFile.PathFor(db));
            CopyTemplates(Path.GetDirectoryName(validation.DatabasePath)!, folder);
            return folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or DatabaseTransferException)
        {
            DiscardStaging(folder);
            throw new DatabaseTransferException(TransferErrorCode.Io, $"The import could not be staged: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Deletes a staged import folder made by <see cref="StageImport"/> (and the <c>import-staging</c> folder when it becomes empty).
    /// Only a folder directly inside an <c>import-staging</c> folder is ever deleted. Failures are ignored.
    /// </summary>
    public void DiscardStaging(string stagingFolder)
    {
        var full = Path.GetFullPath(stagingFolder);
        var parent = Path.GetDirectoryName(full);
        if (parent is null || !string.Equals(Path.GetFileName(parent), StagingFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (Directory.Exists(full))
            {
                Directory.Delete(full, recursive: true);
            }

            if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
            {
                Directory.Delete(parent);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover staging folder is removed at the next start.
        }
    }

    /// <summary>
    /// Runs the pending step of <c>location.json</c>, if any. Call it at startup before anything opens the database.
    /// The pending step is always cleared afterwards. On failure nothing switches and the current database is unchanged.
    /// Returns null when nothing was pending (or <c>location.json</c> is damaged: resolve that first).
    /// <paramref name="currentKey"/> is the key of the current database (the source of a move or replace, and of the backup before an import);
    /// without it an encrypted database is byte-copied (see <see cref="CopyClosedVerified"/>).
    /// A <see cref="PendingKind.Import"/> step whose source is a staged folder (<see cref="StageImport"/>) needs no key or password: the staged
    /// database was verified when it was staged and is copied byte for byte. The staged folder is deleted whether the step succeeds or fails,
    /// and any other folder left in <c>import-staging</c> is deleted at every call.
    /// </summary>
    public PendingOutcome? ApplyPending(DatabaseLocation location, DatabaseKey? currentKey = null)
    {
        var read = location.Read();
        if (read.IsDamaged)
        {
            return null;
        }

        var step = read.Settings.Pending;
        var staged = step is { Kind: PendingKind.Import } && IsStagedFolder(step.Source) ? Path.GetFullPath(step.Source) : null;
        CleanStaging(Path.GetDirectoryName(location.ResolveDatabasePath())!, staged);
        if (step is null)
        {
            return null;
        }

        PendingOutcome outcome;
        try
        {
            switch (step.Kind)
            {
                case PendingKind.Move:
                    ApplyMove(location, step, currentKey);
                    break;
                case PendingKind.Replace:
                    ApplyReplace(location, step, currentKey);
                    break;
                case PendingKind.Import:
                    ApplyImport(step, currentKey);
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

        if (staged is not null)
        {
            DiscardStaging(staged);
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

    private static bool IsStagedFolder(string path)
    {
        var full = Path.GetFullPath(path);
        return Directory.Exists(full)
            && string.Equals(Path.GetFileName(Path.GetDirectoryName(full)), StagingFolderName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Deletes every staged import folder in <paramref name="dataFolder"/> except <paramref name="keep"/>.</summary>
    private void CleanStaging(string dataFolder, string? keep)
    {
        var root = Path.Combine(dataFolder, StagingFolderName);
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            foreach (var folder in Directory.EnumerateDirectories(root).ToList())
            {
                if (keep is null || !string.Equals(Path.GetFullPath(folder), keep, StringComparison.OrdinalIgnoreCase))
                {
                    DiscardStaging(folder);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: retried at the next start.
        }
    }

    private void ApplyMove(DatabaseLocation location, PendingStep step, DatabaseKey? currentKey)
    {
        CopyVerified(step.Source, step.Target, currentKey);
        CopyTemplates(Path.GetDirectoryName(Path.GetFullPath(step.Source))!, Path.GetDirectoryName(Path.GetFullPath(step.Target))!);
        location.SetFolder(Path.GetDirectoryName(Path.GetFullPath(step.Target)));
    }

    private void ApplyReplace(DatabaseLocation location, PendingStep step, DatabaseKey? currentKey)
    {
        var target = Path.GetFullPath(step.Target);
        var incoming = target + ".incoming";
        var incomingKeyFile = DatabaseKeyFile.PathFor(target) + ".incoming";
        try
        {
            DeleteWithSidecars(incoming);
            TryDelete(incomingKeyFile);
            var hasKeyFile = StageIncoming(step.Source, incoming, incomingKeyFile, currentKey);
            if (File.Exists(target))
            {
                // The key of the database being replaced is not known here, so an encrypted one is byte-copied with its key file.
                BackupExisting(target, "replace");
            }

            SwapIn(incoming, target, hasKeyFile ? incomingKeyFile : null);
        }
        finally
        {
            DeleteWithSidecarsQuietly(incoming);
            TryDelete(incomingKeyFile);
        }

        CopyTemplates(Path.GetDirectoryName(Path.GetFullPath(step.Source))!, Path.GetDirectoryName(target)!);
        location.SetFolder(Path.GetDirectoryName(target));
    }

    private void ApplyImport(PendingStep step, DatabaseKey? currentKey)
    {
        ImportValidation? validation = null;
        try
        {
            string sourceDb;
            DatabaseKey? sourceKey = null;
            if (IsStagedFolder(step.Source))
            {
                sourceDb = Path.Combine(Path.GetFullPath(step.Source), ZipDatabaseEntry);
                if (!File.Exists(sourceDb))
                {
                    throw new DatabaseTransferException(TransferErrorCode.NoDatabase, $"The staged import '{step.Source}' has no database.");
                }
            }
            else
            {
                validation = ValidateImport(step.Source);
                if (!validation.IsValid)
                {
                    throw new DatabaseTransferException(validation.Error!.Value, $"The backup file '{step.Source}' cannot be imported ({validation.Error}).");
                }

                sourceDb = validation.DatabasePath!;
                sourceKey = validation.Key;
            }

            var target = Path.GetFullPath(step.Target);
            var incoming = target + ".incoming";
            var incomingKeyFile = DatabaseKeyFile.PathFor(target) + ".incoming";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                DeleteWithSidecars(incoming);
                TryDelete(incomingKeyFile);
                var hasKeyFile = StageIncoming(sourceDb, incoming, incomingKeyFile, sourceKey);
                if (File.Exists(target))
                {
                    BackupExisting(target, "import", currentKey);
                }

                SwapIn(incoming, target, hasKeyFile ? incomingKeyFile : null);
            }
            finally
            {
                DeleteWithSidecarsQuietly(incoming);
                TryDelete(incomingKeyFile);
            }

            CopyTemplates(Path.GetDirectoryName(sourceDb)!, Path.GetDirectoryName(target)!);
        }
        finally
        {
            validation?.Dispose();
        }
    }

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="incoming"/> (verified; byte copy of the closed file when it is encrypted and
    /// <paramref name="key"/> is null) and its key file, if any, to <paramref name="incomingKeyFile"/>. Returns whether a key file was staged.
    /// </summary>
    private bool StageIncoming(string source, string incoming, string incomingKeyFile, DatabaseKey? key)
    {
        source = Path.GetFullPath(source);
        CheckCopyPaths(source, incoming);
        var encrypted = IsEncrypted(source, key);
        var temp = incoming + ".tmp";
        try
        {
            if (encrypted && key is null)
            {
                RequireNoPendingWal(source);
                DeleteWithSidecars(temp);
                CopyFileVerified(source, temp);
            }
            else
            {
                StageVerified(source, temp, encrypted ? key : null);
            }

            var sourceKeyFile = DatabaseKeyFile.PathFor(source);
            var hasKeyFile = encrypted && File.Exists(sourceKeyFile);
            if (hasKeyFile)
            {
                CopyFileVerified(sourceKeyFile, incomingKeyFile);
            }

            File.Move(temp, incoming);
            return hasKeyFile;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            DeleteWithSidecarsQuietly(temp);
            TryDelete(incomingKeyFile);
            throw Translate(ex, incoming, encrypted ? key : null);
        }
    }

    /// <summary>
    /// Puts <paramref name="incoming"/> (and <paramref name="incomingKeyFile"/>, when given) at <paramref name="target"/> (and its key file).
    /// An existing target (with its -wal and -shm files) and an existing key file are renamed aside to <c>.replaced-&lt;timestamp&gt;</c> first,
    /// and everything is restored if a move fails. Nothing is deleted.
    /// </summary>
    private void SwapIn(string incoming, string target, string? incomingKeyFile)
    {
        string[] suffixes = ["", "-wal", "-shm"];
        var targetKeyFile = DatabaseKeyFile.PathFor(target);
        var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var done = new List<(string Now, string Original)>();
        try
        {
            var tag = $".replaced-{stamp}";
            for (var n = 2; File.Exists(target + tag) || File.Exists(targetKeyFile + tag); n++)
            {
                tag = $".replaced-{stamp}-{n}";
            }

            foreach (var suffix in suffixes)
            {
                if (File.Exists(target + suffix))
                {
                    File.Move(target + suffix, target + tag + suffix);
                    done.Add((target + tag + suffix, target + suffix));
                }
            }

            if (File.Exists(targetKeyFile))
            {
                File.Move(targetKeyFile, targetKeyFile + tag);
                done.Add((targetKeyFile + tag, targetKeyFile));
            }

            if (incomingKeyFile is not null)
            {
                File.Move(incomingKeyFile, targetKeyFile);
                done.Add((targetKeyFile, incomingKeyFile));
            }

            File.Move(incoming, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var (current, original) in done.AsEnumerable().Reverse())
            {
                try
                {
                    File.Move(current, original);
                }
                catch (Exception restore) when (restore is IOException or UnauthorizedAccessException)
                {
                    // Best effort: the renamed file stays next to the target.
                }
            }

            throw new DatabaseTransferException(TransferErrorCode.Io, $"The database could not be replaced: {ex.Message}", ex);
        }
    }

    private ImportValidation ValidateEnvelope(string temp, string path, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return Fail(temp, TransferErrorCode.PasswordRequired);
        }

        var status = BackupEnvelope.TryDecrypt(File.ReadAllBytes(path), password, out var zipBytes);
        if (status != BackupDecryptStatus.Success)
        {
            return Fail(temp, status == BackupDecryptStatus.Damaged ? TransferErrorCode.Damaged : TransferErrorCode.WrongPassword);
        }

        try
        {
            using var archive = new ZipArchive(new MemoryStream(zipBytes!), ZipArchiveMode.Read);
            var dbPath = ExtractArchive(archive, temp, withKeyFile: true);
            if (dbPath is null)
            {
                return Fail(temp, TransferErrorCode.NoDatabase);
            }

            string? hex = null;
            if (archive.GetEntry(ZipKeyEntry) is { } keyEntry)
            {
                using var reader = new StreamReader(keyEntry.Open(), Encoding.UTF8);
                hex = reader.ReadToEnd().Trim();
            }

            var key = DatabaseKey.FromHex(hex);
            DatabaseKeyFileContents? keyFile;
            try
            {
                keyFile = DatabaseKeyFile.Read(dbPath);
            }
            catch (DatabaseKeyFileException)
            {
                keyFile = null;
            }

            if (key is null || keyFile is null || !IsEncrypted(dbPath, key))
            {
                return Fail(temp, TransferErrorCode.Damaged);
            }

            return Finish(temp, dbPath, key, keyFile);
        }
        catch (InvalidDataException)
        {
            return Fail(temp, TransferErrorCode.Damaged);
        }
    }

    /// <summary>Inspects the extracted database and builds the validation result.</summary>
    private ImportValidation Finish(string temp, string dbPath, DatabaseKey? key, DatabaseKeyFileContents? keyFile)
    {
        var inspection = Inspect(dbPath, key);
        if (inspection.KeyProblem is { } problem)
        {
            // A key from inside the backup that does not open its own database means a damaged backup.
            return Fail(temp, key is null ? problem : TransferErrorCode.Damaged, inspection);
        }

        if (!inspection.IntegrityOk)
        {
            return Fail(temp, TransferErrorCode.Damaged, inspection);
        }

        if (inspection.IsNewerThanApp)
        {
            return Fail(temp, TransferErrorCode.NewerVersion, inspection);
        }

        return new ImportValidation(temp, dbPath, Path.Combine(temp, TemplatesFolderName), inspection, null, key, keyFile);
    }

    /// <summary>Extracts the database (and, with <paramref name="withKeyFile"/>, its key file) and the templates. Returns the database path, or null when there is none.</summary>
    private static string? ExtractArchive(ZipArchive archive, string temp, bool withKeyFile)
    {
        var dbEntry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, ZipDatabaseEntry, StringComparison.OrdinalIgnoreCase));
        if (dbEntry is null)
        {
            return null;
        }

        var dbPath = Path.Combine(temp, ZipDatabaseEntry);
        dbEntry.ExtractToFile(dbPath);

        if (withKeyFile)
        {
            var keyFileEntry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, ZipKeyFileEntry, StringComparison.OrdinalIgnoreCase));
            keyFileEntry?.ExtractToFile(DatabaseKeyFile.PathFor(dbPath));
        }

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

        return dbPath;
    }

    private static void AddDatabaseAndTemplates(ZipArchive archive, string dbCopy, string dataFolder)
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

    private static bool HasEnvelopeMagic(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var head = new byte[4];
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        return read == head.Length && BackupEnvelope.HasMagic(head);
    }

    /// <summary>
    /// True when the file is treated as an encrypted database: it has a non-SQLite header and either a key is given or a key file sits next to it.
    /// (A garbage file with neither stays "plain", so it is reported as damaged as before.)
    /// </summary>
    private static bool IsEncrypted(string path, DatabaseKey? key)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        return DatabaseFormatDetector.Detect(path) == DatabaseFileFormat.Encrypted && (key is not null || File.Exists(DatabaseKeyFile.PathFor(path)));
    }

    private static void CheckCopyPaths(string sourceDbPath, string targetDbPath)
    {
        if (!File.Exists(sourceDbPath))
        {
            throw new DatabaseTransferException(TransferErrorCode.SourceMissing, $"The database '{sourceDbPath}' does not exist.");
        }

        if (File.Exists(targetDbPath))
        {
            throw new DatabaseTransferException(TransferErrorCode.TargetExists, $"The database '{targetDbPath}' already exists.");
        }

        if (File.Exists(DatabaseKeyFile.PathFor(sourceDbPath)) && File.Exists(DatabaseKeyFile.PathFor(targetDbPath))
            && !string.Equals(DatabaseKeyFile.PathFor(sourceDbPath), DatabaseKeyFile.PathFor(targetDbPath), StringComparison.OrdinalIgnoreCase)
            && DatabaseFormatDetector.Detect(sourceDbPath) == DatabaseFileFormat.Encrypted)
        {
            throw new DatabaseTransferException(TransferErrorCode.TargetExists, $"The key file '{DatabaseKeyFile.PathFor(targetDbPath)}' already exists.");
        }
    }

    private static void RequireNoPendingWal(string dbPath)
    {
        if (File.Exists(dbPath + "-wal") && new FileInfo(dbPath + "-wal").Length > 0)
        {
            throw new InvalidDataException("The database is in use or was not closed cleanly (it has a pending -wal file); it cannot be copied without its key.");
        }
    }

    /// <summary>Copies the database to <paramref name="temp"/> with the backup API (same key on both sides) and checks the copy with <c>integrity_check</c>.</summary>
    private static void StageVerified(string source, string temp, DatabaseKey? key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        DeleteWithSidecars(temp);

        using (var from = DatabaseConnections.Open(source, SqliteOpenMode.ReadOnly, key))
        using (var to = DatabaseConnections.Open(temp, SqliteOpenMode.ReadWriteCreate, key))
        {
            from.BackupDatabase(to);
        }

        using var check = DatabaseConnections.Open(temp, SqliteOpenMode.ReadOnly, key);
        var result = Scalar(check, "PRAGMA integrity_check;") as string;
        if (result != "ok")
        {
            throw new InvalidDataException($"The copy failed the integrity check: {result}");
        }
    }

    /// <summary>Copies a file to <paramref name="destination"/> (replacing a leftover temp file) and checks length and SHA-256.</summary>
    private static void CopyFileVerified(string source, string destination)
    {
        File.Copy(source, destination, overwrite: true);
        if (new FileInfo(source).Length != new FileInfo(destination).Length || !HashOf(source).AsSpan().SequenceEqual(HashOf(destination)))
        {
            File.Delete(destination);
            throw new InvalidDataException($"The copy of '{Path.GetFileName(source)}' does not match the original.");
        }
    }

    private static byte[] HashOf(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return SHA256.HashData(stream);
    }

    private static DatabaseTransferException Translate(Exception ex, string targetDbPath, DatabaseKey? key)
    {
        if (ex is SqliteException { SqliteErrorCode: 26 } && key is not null)
        {
            return new DatabaseTransferException(TransferErrorCode.KeyRejected, "The key does not open the database.", ex);
        }

        if (ex is IOException && File.Exists(targetDbPath))
        {
            return new DatabaseTransferException(TransferErrorCode.TargetExists, $"The database '{targetDbPath}' already exists.", ex);
        }

        return new DatabaseTransferException(TransferErrorCode.CopyFailed, $"The database could not be copied: {ex.Message}", ex);
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
