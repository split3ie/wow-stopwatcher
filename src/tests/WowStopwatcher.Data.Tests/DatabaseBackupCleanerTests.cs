namespace WowStopwatcher.Data.Tests;

public class DatabaseBackupCleanerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static void WithFolder(Action<string, string> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "wowsw-cleaner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            body(root, Path.Combine(root, "x.db"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string Make(string root, string name, TimeSpan age)
    {
        var folder = Path.Combine(root, "backups");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, name);
        File.WriteAllText(file, "x");
        File.SetLastWriteTimeUtc(file, (Now - age).UtcDateTime);
        return file;
    }

    private static void Clean(string dbPath) =>
        new DatabaseBackupCleaner(dbPath, new FixedTimeProvider(Now)).DeleteOlderThan(DatabaseBackupCleaner.DefaultMaxAge);

    [Fact]
    public void DeletesOldBackupWithSidecars_KeepsRecent()
    {
        WithFolder((root, db) =>
        {
            var old = Make(root, "x-v6.db", TimeSpan.FromDays(8));
            var wal = Make(root, "x-v6.db-wal", TimeSpan.FromDays(8));
            var shm = Make(root, "x-v6.db-shm", TimeSpan.FromDays(8));
            var recent = Make(root, "x-v6-2.db", TimeSpan.FromDays(6));

            Clean(db);

            Assert.False(File.Exists(old));
            Assert.False(File.Exists(wal));
            Assert.False(File.Exists(shm));
            Assert.True(File.Exists(recent));
        });
    }

    [Fact]
    public void DeletesBackupExactlySevenDaysOld()
    {
        WithFolder((root, db) =>
        {
            var file = Make(root, "x-v3.db", TimeSpan.FromDays(7));
            Clean(db);
            Assert.False(File.Exists(file));
        });
    }

    [Fact]
    public void NeverTouchesNonMatchingFiles()
    {
        WithFolder((root, db) =>
        {
            var names = new[] { "notes.txt", "x.db", "other-v6.db", "x-v6.db.bak", "x-vA.db" };
            var files = names.Select(n => Make(root, n, TimeSpan.FromDays(30))).ToList();

            Clean(db);

            Assert.All(files, f => Assert.True(File.Exists(f), f));
        });
    }

    [Fact]
    public void MissingBackupsFolder_DoesNothing()
    {
        WithFolder((root, db) =>
        {
            Clean(db);
            Assert.False(Directory.Exists(Path.Combine(root, "backups")));
        });
    }
}
