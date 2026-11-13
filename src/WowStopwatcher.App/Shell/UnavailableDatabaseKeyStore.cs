using WowStopwatcher.Core.Security;

namespace WowStopwatcher.App.Shell;

/// <summary>Used on systems without a supported secret store.</summary>
public sealed class UnavailableDatabaseKeyStore : IDatabaseKeyStore
{
    public bool IsAvailable => false;

    public DatabaseKey? Get(Guid databaseId) => null;

    public void Save(Guid databaseId, DatabaseKey key) => throw new DatabaseKeyStoreException("No secret store is available on this system.");

    public void Delete(Guid databaseId) => throw new DatabaseKeyStoreException("No secret store is available on this system.");
}
