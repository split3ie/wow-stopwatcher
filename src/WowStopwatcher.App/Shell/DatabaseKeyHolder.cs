using WowStopwatcher.Core.Security;

namespace WowStopwatcher.App.Shell;

/// <summary>Holds the key of the open database for the rest of the app. The startup code calls <see cref="Set"/> once.</summary>
public sealed class DatabaseKeyHolder : IDatabaseKeyProvider
{
    public DatabaseKey? Key { get; private set; }

    public Guid? DatabaseId { get; private set; }

    public void Set(DatabaseKey key, Guid databaseId)
    {
        Key = key;
        DatabaseId = databaseId;
    }
}
