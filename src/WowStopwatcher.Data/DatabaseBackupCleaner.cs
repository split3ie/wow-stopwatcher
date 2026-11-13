using System.Text.RegularExpressions;

namespace WowStopwatcher.Data;

/// <summary>
/// Deletes old migration backups (<c>backups/&lt;name&gt;-v&lt;N&gt;.db</c> or <c>-v&lt;N&gt;-&lt;k&gt;.db</c>) next to the database.
/// Only files matching that name pattern are touched. Failures to delete are ignored.
/// </summary>
public sealed class DatabaseBackupCleaner
{
    /// <summary>Backups older than this are deleted at startup.</summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(7);

    private readonly string _databasePath;
    private readonly TimeProvider _time;

    public DatabaseBackupCleaner(string databasePath, TimeProvider time)
    {
        _databasePath = Path.GetFullPath(databasePath);
        _time = time;
    }

    /// <summary>Deletes backups last written at least <paramref name="maxAge"/> ago, with their -wal and -shm files and key file (<c>.key.json</c>).</summary>
    public void DeleteOlderThan(TimeSpan maxAge)
    {
        var folder = Path.Combine(Path.GetDirectoryName(_databasePath)!, "backups");
        if (!Directory.Exists(folder))
        {
            return;
        }

        var name = Path.GetFileNameWithoutExtension(_databasePath);
        var pattern = new Regex($@"^{Regex.Escape(name)}-v\d+(-\d+)?\.db$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var now = _time.GetUtcNow();

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            if (!pattern.IsMatch(Path.GetFileName(file)))
            {
                continue;
            }

            try
            {
                if (now - File.GetLastWriteTimeUtc(file) < maxAge)
                {
                    continue;
                }

                File.Delete(file + "-wal");
                File.Delete(file + "-shm");
                File.Delete(file);
                File.Delete(DatabaseKeyFile.PathFor(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: a backup that cannot be deleted now is retried at the next start.
            }
        }
    }
}
