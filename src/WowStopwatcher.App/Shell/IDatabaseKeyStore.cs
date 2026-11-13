using WowStopwatcher.Core.Security;

namespace WowStopwatcher.App.Shell;

/// <summary>Keeps the database encryption key of each database in the OS secret store.</summary>
public interface IDatabaseKeyStore
{
    /// <summary>False when the platform store cannot be used (for example Linux without a Secret Service).</summary>
    bool IsAvailable { get; }

    /// <summary>The stored key, or null when there is no entry. Throws <see cref="DatabaseKeyStoreException"/> when the store cannot be read.</summary>
    DatabaseKey? Get(Guid databaseId);

    /// <summary>Stores the key, replacing an existing entry. Throws <see cref="DatabaseKeyStoreException"/> on failure.</summary>
    void Save(Guid databaseId, DatabaseKey key);

    /// <summary>Removes the entry; nothing happens when there is none. Throws <see cref="DatabaseKeyStoreException"/> on failure.</summary>
    void Delete(Guid databaseId);
}
