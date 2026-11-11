using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace WowStopwatcher.Data.Tests;

public sealed class DatabaseTransferTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 14, 30, 5, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "wowsw-transfer-" + Guid.NewGuid().ToString("N"));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private DatabaseTransfer Transfer => new(new FixedTimeProvider(Now));

    private string Folder(string name) => Path.Combine(_root, name);

    private DatabaseLocation Location() => new(Folder("default"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static string Db(string folder) => Path.Combine(folder, "wowstopwatcher.db");

    /// <summary>Creates a migrated database with <paramref name="stopwatches"/> stopwatches and one session per given start.</summary>
    private static void Create(string folder, int stopwatches, params DateTimeOffset[] sessionStarts)
    {
        var database = new SqliteDatabase(Db(folder));
        using var connection = database.OpenConnection();
        for (var i = 0; i < stopwatches; i++)
        {
            Exec(connection, $"INSERT INTO stopwatches (id, name, color, sort_order) VALUES ('s{i}', 'S{i}', '#2F6FEB', {i});");
        }

        foreach (var start in sessionStarts)
        {
            Exec(connection, $"INSERT INTO sessions (stopwatch_id, start_utc, stop_utc) VALUES ('s0', {start.ToUnixTimeMilliseconds()}, {start.ToUnixTimeMilliseconds() + 1000});");
        }
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void SetUserVersion(string path, int version)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        Exec(connection, $"PRAGMA user_version = {version};");
    }

    private static void WriteTemplate(string folder, string name, string content)
    {
        var file = Path.Combine(folder, "templates", name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    [Fact]
    public void CopyVerified_WithOpenWalSource_CopiesUncheckpointedData()
    {
        var source = Folder("src");
        var database = new SqliteDatabase(Db(source));
        using var open = database.OpenConnection();
        Exec(open, "INSERT INTO stopwatches (id, name, color, sort_order) VALUES ('a', 'A', '#fff', 0);");
        Assert.True(File.Exists(Db(source) + "-wal"));

        var target = Db(Folder("dst"));
        Transfer.CopyVerified(Db(source), target);

        Assert.Equal(1, Transfer.Inspect(target).StopwatchCount);
        Assert.False(File.Exists(target + ".tmp"));
    }

    [Fact]
    public void CopyVerified_RefusesToOverwrite()
    {
        Create(Folder("src"), 1);
        Create(Folder("dst"), 2);

        var ex = Assert.Throws<DatabaseTransferException>(() => Transfer.CopyVerified(Db(Folder("src")), Db(Folder("dst"))));

        Assert.Equal(TransferErrorCode.TargetExists, ex.Code);
        Assert.Equal(2, Transfer.Inspect(Db(Folder("dst"))).StopwatchCount);
    }

    [Fact]
    public void CopyVerified_MissingSourceOrNotADatabase_Throws_NoTargetLeft()
    {
        Directory.CreateDirectory(Folder("src"));
        var missing = Assert.Throws<DatabaseTransferException>(() => Transfer.CopyVerified(Db(Folder("src")), Db(Folder("dst"))));
        Assert.Equal(TransferErrorCode.SourceMissing, missing.Code);

        File.WriteAllText(Db(Folder("src")), "this is not a database, just some text padding to be long enough");
        var bad = Assert.Throws<DatabaseTransferException>(() => Transfer.CopyVerified(Db(Folder("src")), Db(Folder("dst"))));
        Assert.Equal(TransferErrorCode.CopyFailed, bad.Code);
        Assert.False(File.Exists(Db(Folder("dst"))));
        Assert.False(File.Exists(Db(Folder("dst")) + ".tmp"));
    }

    [Fact]
    public void CopyTemplates_CopiesNew_KeepsDifferentExisting()
    {
        WriteTemplate(Folder("src"), "a.docx", "new a");
        WriteTemplate(Folder("src"), Path.Combine("sub", "b.docx"), "b");
        WriteTemplate(Folder("dst"), "a.docx", "mine");

        var skipped = Transfer.CopyTemplates(Folder("src"), Folder("dst"));

        Assert.Equal(1, skipped);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(Folder("dst"), "templates", "a.docx")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(Folder("dst"), "templates", "sub", "b.docx")));
        Assert.Equal(0, Transfer.CopyTemplates(Folder("none"), Folder("dst")));
    }

    [Fact]
    public void Inspect_ReportsCounts()
    {
        var day1 = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var day2 = new DateTimeOffset(2026, 3, 12, 12, 0, 0, TimeSpan.Zero);
        Create(Folder("a"), 3, day1, day1.AddHours(1), day2);

        var info = Transfer.Inspect(Db(Folder("a")));

        Assert.Equal(SqliteDatabase.LatestSchemaVersion, info.SchemaVersion);
        Assert.False(info.IsNewerThanApp);
        Assert.True(info.IntegrityOk);
        Assert.Equal(3, info.StopwatchCount);
        Assert.Equal(2, info.SessionDayCount);
        Assert.Equal(day2, info.LastSessionStartUtc);
    }

    [Fact]
    public void Inspect_EmptyDatabase_HasNoLastSession()
    {
        Create(Folder("a"), 0);

        var info = Transfer.Inspect(Db(Folder("a")));

        Assert.Equal(0, info.SessionDayCount);
        Assert.Null(info.LastSessionStartUtc);
    }

    [Fact]
    public void Inspect_NewerSchema_NeverWrites()
    {
        Create(Folder("a"), 1);
        SetUserVersion(Db(Folder("a")), SqliteDatabase.LatestSchemaVersion + 1);
        var before = File.ReadAllBytes(Db(Folder("a")));

        var info = Transfer.Inspect(Db(Folder("a")));

        Assert.True(info.IsNewerThanApp);
        Assert.Equal(before, File.ReadAllBytes(Db(Folder("a"))));
    }

    [Fact]
    public void Inspect_NotADatabase_IsNotIntact()
    {
        Directory.CreateDirectory(Folder("b"));
        File.WriteAllText(Db(Folder("b")), "plain text that is certainly not an sqlite database file at all");

        Assert.False(Transfer.Inspect(Db(Folder("b"))).IntegrityOk);
    }

    [Fact]
    public void BackupExisting_NamesFileAndCleanerKeepsIt()
    {
        Create(Folder("a"), 2);

        var backup = Transfer.BackupExisting(Db(Folder("a")), "import");

        Assert.Equal(Path.Combine(Folder("a"), "backups", "wowstopwatcher-before-import-20261111-143005.db"), backup);
        Assert.Equal(2, Transfer.Inspect(backup).StopwatchCount);

        var second = Transfer.BackupExisting(Db(Folder("a")), "import");
        Assert.NotEqual(backup, second);

        var later = new FixedTimeProvider(Now.AddDays(365));
        new DatabaseBackupCleaner(Db(Folder("a")), later).DeleteOlderThan(TimeSpan.Zero);
        File.SetLastWriteTimeUtc(backup, Now.AddDays(-30).UtcDateTime);
        new DatabaseBackupCleaner(Db(Folder("a")), later).DeleteOlderThan(DatabaseBackupCleaner.DefaultMaxAge);

        Assert.True(File.Exists(backup));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public void BackupExisting_MissingSource_ThrowsBackupFailed()
    {
        Directory.CreateDirectory(Folder("a"));

        var ex = Assert.Throws<DatabaseTransferException>(() => Transfer.BackupExisting(Db(Folder("a")), "replace"));

        Assert.Equal(TransferErrorCode.BackupFailed, ex.Code);
    }

    [Fact]
    public void ExportThenValidate_RoundTripsDatabaseAndTemplates()
    {
        var source = Folder("a");
        var database = new SqliteDatabase(Db(source));
        using var open = database.OpenConnection();
        Exec(open, "INSERT INTO stopwatches (id, name, color, sort_order) VALUES ('a', 'A', '#fff', 0);");
        WriteTemplate(source, Path.Combine("c", "t.docx"), "template");
        var zip = Path.Combine(_root, "out", "backup.zip");

        Transfer.Export(Db(source), source, zip);

        Assert.False(File.Exists(zip + ".tmp"));
        using (var archive = ZipFile.OpenRead(zip))
        {
            var names = archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
            Assert.Equal(["templates/c/t.docx", "wowstopwatcher.db"], names);
        }

        using var validation = Transfer.ValidateImport(zip);
        Assert.True(validation.IsValid);
        Assert.Equal(1, validation.Inspection!.StopwatchCount);
        Assert.Equal("template", File.ReadAllText(Path.Combine(validation.TemplatesFolder!, "c", "t.docx")));
        var tempFolder = Path.GetDirectoryName(validation.DatabasePath!)!;
        validation.Dispose();
        Assert.False(Directory.Exists(tempFolder));
    }

    [Fact]
    public void ValidateImport_RejectsNotAZip_NoDatabase_Newer_Damaged_Missing()
    {
        Directory.CreateDirectory(_root);
        var text = Path.Combine(_root, "text.zip");
        File.WriteAllText(text, "definitely not a zip file");
        using (var v = Transfer.ValidateImport(text))
        {
            Assert.Equal(TransferErrorCode.NotAZip, v.Error);
        }

        var empty = Path.Combine(_root, "empty.zip");
        using (var archive = ZipFile.Open(empty, ZipArchiveMode.Create))
        {
            archive.CreateEntry("readme.txt");
        }

        using (var v = Transfer.ValidateImport(empty))
        {
            Assert.Equal(TransferErrorCode.NoDatabase, v.Error);
        }

        Create(Folder("n"), 1);
        SetUserVersion(Db(Folder("n")), SqliteDatabase.LatestSchemaVersion + 1);
        var newer = Path.Combine(_root, "newer.zip");
        using (var archive = ZipFile.Open(newer, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(Db(Folder("n")), "wowstopwatcher.db");
        }

        using (var v = Transfer.ValidateImport(newer))
        {
            Assert.Equal(TransferErrorCode.NewerVersion, v.Error);
            Assert.False(v.IsValid);
        }

        var junk = Path.Combine(_root, "junk.db");
        File.WriteAllText(junk, "not sqlite but long enough to look like a file with some content in it");
        var damaged = Path.Combine(_root, "damaged.zip");
        using (var archive = ZipFile.Open(damaged, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(junk, "wowstopwatcher.db");
        }

        using (var v = Transfer.ValidateImport(damaged))
        {
            Assert.Equal(TransferErrorCode.Damaged, v.Error);
        }

        using (var v = Transfer.ValidateImport(Path.Combine(_root, "nothing.zip")))
        {
            Assert.Equal(TransferErrorCode.SourceMissing, v.Error);
        }
    }

    [Fact]
    public void ApplyPending_NothingPending_ReturnsNull()
    {
        Assert.Null(Transfer.ApplyPending(Location()));
    }

    [Fact]
    public void ApplyPending_Move_CopiesAndSwitches()
    {
        var location = Location();
        Create(location.DefaultFolder, 2);
        WriteTemplate(location.DefaultFolder, "t.docx", "t");
        var target = Db(Folder("new"));
        location.SetPending(new PendingStep(PendingKind.Move, location.DefaultDatabasePath, target));

        var outcome = Transfer.ApplyPending(location);

        Assert.True(outcome!.Success);
        Assert.Equal(target, location.ResolveDatabasePath());
        Assert.Null(location.Read().Settings.Pending);
        Assert.Equal(2, Transfer.Inspect(target).StopwatchCount);
        Assert.Equal("t", File.ReadAllText(Path.Combine(Folder("new"), "templates", "t.docx")));
        Assert.True(File.Exists(location.DefaultDatabasePath));
    }

    [Fact]
    public void ApplyPending_Move_ToExistingDatabase_FailsAndChangesNothing()
    {
        var location = Location();
        Create(location.DefaultFolder, 1);
        Create(Folder("new"), 5);
        var target = Db(Folder("new"));
        var targetBytes = File.ReadAllBytes(target);
        location.SetPending(new PendingStep(PendingKind.Move, location.DefaultDatabasePath, target));

        var outcome = Transfer.ApplyPending(location);

        Assert.False(outcome!.Success);
        Assert.Equal(TransferErrorCode.TargetExists, outcome.Error);
        Assert.Null(location.Read().Settings.Pending);
        Assert.Null(location.Read().Settings.DatabaseFolder);
        Assert.Equal(location.DefaultDatabasePath, location.ResolveDatabasePath());
        Assert.Equal(targetBytes, File.ReadAllBytes(target));
        Assert.Equal(1, Transfer.Inspect(location.DefaultDatabasePath).StopwatchCount);
    }

    [Fact]
    public void ApplyPending_Replace_BacksUpThenReplacesAndSwitches()
    {
        var location = Location();
        Create(location.DefaultFolder, 1);
        Create(Folder("new"), 5);
        var target = Db(Folder("new"));
        location.SetPending(new PendingStep(PendingKind.Replace, location.DefaultDatabasePath, target));

        var outcome = Transfer.ApplyPending(location);

        Assert.True(outcome!.Success);
        Assert.Equal(target, location.ResolveDatabasePath());
        Assert.Equal(1, Transfer.Inspect(target).StopwatchCount);
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(Folder("new"), "backups"), "*.db"));
        Assert.Contains("-before-replace-", Path.GetFileName(backup));
        Assert.Equal(5, Transfer.Inspect(backup).StopwatchCount);
        Assert.Single(Directory.GetFiles(Folder("new"), "wowstopwatcher.db.replaced-*"), f => !f.EndsWith("-wal") && !f.EndsWith("-shm"));
        Assert.False(File.Exists(target + ".incoming"));
    }

    [Fact]
    public void ApplyPending_Replace_MissingSource_LeavesTargetUntouched()
    {
        var location = Location();
        Create(Folder("new"), 5);
        var target = Db(Folder("new"));
        var bytes = File.ReadAllBytes(target);
        location.SetPending(new PendingStep(PendingKind.Replace, location.DefaultDatabasePath, target));

        var outcome = Transfer.ApplyPending(location);

        Assert.Equal(TransferErrorCode.SourceMissing, outcome!.Error);
        Assert.Equal(bytes, File.ReadAllBytes(target));
        Assert.False(Directory.Exists(Path.Combine(Folder("new"), "backups")));
        Assert.Null(location.Read().Settings.DatabaseFolder);
        Assert.Null(location.Read().Settings.Pending);
    }

    [Fact]
    public void ApplyPending_Import_BacksUpCurrentAndReplaces_WithTemplates()
    {
        var location = Location();
        Create(Folder("export"), 4);
        WriteTemplate(Folder("export"), "t.docx", "from zip");
        var zip = Path.Combine(_root, "backup.zip");
        Transfer.Export(Db(Folder("export")), Folder("export"), zip);

        Create(location.DefaultFolder, 1);
        location.SetPending(new PendingStep(PendingKind.Import, zip, location.DefaultDatabasePath));

        var outcome = Transfer.ApplyPending(location);

        Assert.True(outcome!.Success);
        Assert.Equal(4, Transfer.Inspect(location.DefaultDatabasePath).StopwatchCount);
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(location.DefaultFolder, "backups"), "*.db"));
        Assert.Contains("-before-import-", Path.GetFileName(backup));
        Assert.Equal(1, Transfer.Inspect(backup).StopwatchCount);
        Assert.Equal("from zip", File.ReadAllText(Path.Combine(location.DefaultFolder, "templates", "t.docx")));
        Assert.Null(location.Read().Settings.Pending);
        Assert.Null(location.Read().Settings.DatabaseFolder);
    }

    [Fact]
    public void ApplyPending_Import_InvalidZip_LeavesEverythingUnchanged()
    {
        var location = Location();
        Create(location.DefaultFolder, 1);
        var bytes = File.ReadAllBytes(location.DefaultDatabasePath);
        var zip = Path.Combine(_root, "bad.zip");
        File.WriteAllText(zip, "not a zip");
        location.SetPending(new PendingStep(PendingKind.Import, zip, location.DefaultDatabasePath));

        var outcome = Transfer.ApplyPending(location);

        Assert.Equal(TransferErrorCode.NotAZip, outcome!.Error);
        Assert.Equal(bytes, File.ReadAllBytes(location.DefaultDatabasePath));
        Assert.False(Directory.Exists(Path.Combine(location.DefaultFolder, "backups")));
        Assert.Null(location.Read().Settings.Pending);
    }
}
