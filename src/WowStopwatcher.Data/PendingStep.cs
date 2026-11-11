namespace WowStopwatcher.Data;

/// <summary>
/// A step stored in <c>location.json</c>. For <see cref="PendingKind.Move"/> and <see cref="PendingKind.Replace"/>,
/// <paramref name="Source"/> is the source database file and <paramref name="Target"/> the database file to create or replace.
/// For <see cref="PendingKind.Import"/>, <paramref name="Source"/> is the backup zip and <paramref name="Target"/> the current database file.
/// </summary>
public sealed record PendingStep(PendingKind Kind, string Source, string Target);
