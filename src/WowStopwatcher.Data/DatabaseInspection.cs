namespace WowStopwatcher.Data;

/// <summary>Read-only summary of a database file for the UI.</summary>
/// <param name="SchemaVersion">The <c>user_version</c> stored in the file.</param>
/// <param name="IsNewerThanApp">True when the schema is newer than <see cref="SqliteDatabase.LatestSchemaVersion"/>.</param>
/// <param name="IntegrityOk">False when the file is damaged or not a database; the counts are then zero.</param>
/// <param name="StopwatchCount">Stopwatches that are not deleted.</param>
/// <param name="SessionDayCount">Distinct local days with at least one session.</param>
/// <param name="LastSessionStartUtc">Start of the latest session, or null when there is none.</param>
public sealed record DatabaseInspection(
    int SchemaVersion,
    bool IsNewerThanApp,
    bool IntegrityOk,
    int StopwatchCount,
    int SessionDayCount,
    DateTimeOffset? LastSessionStartUtc);
