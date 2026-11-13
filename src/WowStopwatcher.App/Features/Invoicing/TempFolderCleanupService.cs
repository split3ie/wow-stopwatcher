using System.Text.RegularExpressions;
using WowStopwatcher.App.Composition;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>
/// At startup removes temporary folders left behind by a crash during PDF generation: directories directly in the temp root named
/// <c>wowstopwatcher-invoice-</c> or <c>wowstopwatcher-lo-</c> plus a 32-character hex GUID and not written to for over an hour.
/// Best effort: a folder that cannot be deleted is skipped.
/// </summary>
public sealed partial class TempFolderCleanupService(string tempRoot, TimeProvider time) : IAppService
{
    /// <summary>A folder must be unused for at least this long before it is deleted.</summary>
    public static readonly TimeSpan MinAge = TimeSpan.FromHours(1);

    public void Start() => Task.Run(() => CleanUp());

    /// <summary>Deletes the matching old folders and returns how many were removed.</summary>
    public int CleanUp()
    {
        var removed = 0;
        IEnumerable<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories(tempRoot).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        var limit = time.GetUtcNow().UtcDateTime - MinAge;
        foreach (var folder in folders)
        {
            try
            {
                if (!NamePattern().IsMatch(Path.GetFileName(folder)) || Directory.GetLastWriteTimeUtc(folder) >= limit)
                {
                    continue;
                }

                Directory.Delete(folder, recursive: true);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: in use or not ours to delete; leave it.
            }
        }

        return removed;
    }

    public void Dispose()
    {
    }

    [GeneratedRegex("^wowstopwatcher-(invoice|lo)-[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
