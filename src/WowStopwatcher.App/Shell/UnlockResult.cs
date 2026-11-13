using WowStopwatcher.Core.Security;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Shell;

/// <summary>The outcome of a successful <see cref="DatabaseUnlock"/>.</summary>
/// <param name="Key">The key that opens the database; null when encryption is off in this build (the database is plain).</param>
/// <param name="DatabaseId">The id of the database (its key store entry); null when encryption is off.</param>
/// <param name="PendingOutcome">The result of a move, replace or import that ran at startup; null when there was none.</param>
/// <param name="PinChoice">The PIN choice made in the "Protect your data" window; null when that window was not shown or had no PIN step.</param>
public sealed record UnlockResult(DatabaseKey? Key, Guid? DatabaseId, PendingOutcome? PendingOutcome, StartupPinChoice? PinChoice);
