using WowStopwatcher.Core.Security;

namespace WowStopwatcher.Data;

/// <summary>What <see cref="DatabaseKeyFile"/> stores: the database id (used to find the key in an OS secret store) and the recovery-code-wrapped key.</summary>
/// <param name="DatabaseId">Random id of this encrypted database.</param>
/// <param name="Wrapped">The database key wrapped by the recovery code.</param>
public sealed record DatabaseKeyFileContents(Guid DatabaseId, WrappedDatabaseKey Wrapped);
