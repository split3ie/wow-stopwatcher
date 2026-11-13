using WowStopwatcher.Core.Security;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Shell;

/// <summary>
/// The steps that must run before the database is opened: check <c>location.json</c>, run the pending step (move, replace or import),
/// and refuse a configured folder that does not exist so that no empty database is ever created at a configured path.
/// </summary>
public sealed class DatabaseStartup(DatabaseLocation location, DatabaseTransfer transfer)
{
    /// <summary>
    /// Returns the result of the pending step (null when there was none) to show once.
    /// <paramref name="currentKey"/> is the key of the database that is open now (null when there is none or it is plain);
    /// the pending step needs it to back up or copy an encrypted database.
    /// </summary>
    /// <exception cref="DatabaseOpenException"><c>location.json</c> is damaged or the configured folder is missing.</exception>
    public PendingOutcome? Run(DatabaseKey? currentKey = null)
    {
        // Throws for a damaged location.json; that is only ever reset by an explicit user choice.
        location.ResolveDatabasePath();

        var outcome = transfer.ApplyPending(location, currentKey);

        if (location.IsConfiguredFolderMissing())
        {
            throw new DatabaseOpenException(
                DatabaseProblem.FolderMissing, location.ResolveDatabasePath(), "The configured database folder does not exist.");
        }

        return outcome;
    }
}
