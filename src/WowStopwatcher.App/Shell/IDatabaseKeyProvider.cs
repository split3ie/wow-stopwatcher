using WowStopwatcher.Core.Security;

namespace WowStopwatcher.App.Shell;

/// <summary>The key of the database that is open. It is set once at startup, after the database was unlocked.</summary>
public interface IDatabaseKeyProvider
{
    /// <summary>The key of the open database; null until startup has unlocked the database.</summary>
    DatabaseKey? Key { get; }

    /// <summary>The id of the open database (the name of its entry in the key store); null until startup has unlocked it.</summary>
    Guid? DatabaseId { get; }
}
