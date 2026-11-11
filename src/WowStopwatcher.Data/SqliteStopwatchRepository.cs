using Microsoft.Data.Sqlite;
using WowStopwatcher.Core;

namespace WowStopwatcher.Data;

/// <summary>
/// SQLite implementation of <see cref="IStopwatchRepository"/>. With <paramref name="currentLanguage"/> names are stored per language:
/// <c>stopwatches.name</c> is the original (in <c>name_language</c>), other languages live in <c>stopwatch_names</c>.
/// Without it only <c>stopwatches.name</c> is used.
/// </summary>
public sealed class SqliteStopwatchRepository(SqliteDatabase database, TimeProvider? time = null, Func<string>? currentLanguage = null) : IStopwatchRepository
{
    private const string Columns = "s.id, COALESCE(n.name, s.name), s.color, s.goal_seconds, s.shortcut, s.sort_order, s.is_deleted, s.is_independent, s.is_permanent, s.active_since_utc, s.goal_since_utc, s.goal_per_cycle";

    private const string From = " FROM stopwatches s LEFT JOIN stopwatch_names n ON n.stopwatch_id = s.id AND n.language = $lang";

    private string? Language => currentLanguage?.Invoke();

    public IReadOnlyList<StopwatchDefinition> GetAll(bool includeDeleted = false)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns}{From}"
            + (includeDeleted ? "" : " WHERE s.is_deleted = 0")
            + " ORDER BY s.sort_order, COALESCE(n.name, s.name), s.id";
        command.Parameters.AddWithValue("$lang", (object?)Language ?? DBNull.Value);
        return ReadAll(command);
    }

    public StopwatchDefinition? Get(string id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns}{From} WHERE s.id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$lang", (object?)Language ?? DBNull.Value);
        return ReadAll(command).FirstOrDefault();
    }

    public void Add(StopwatchDefinition stopwatch)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO stopwatches (id, name, name_language, color, goal_seconds, shortcut, sort_order, is_deleted, is_independent, is_permanent, active_since_utc, goal_since_utc, goal_per_cycle)
            VALUES ($id, $name, $lang, $color, $goal, $shortcut, $sort, $deleted, $independent, $permanent, $active, $goalSince, $perCycle)
            """;
        Bind(command, stopwatch);
        command.Parameters.AddWithValue("$lang", (object?)Language ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void Update(StopwatchDefinition stopwatch)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        RecordGoalChange(connection, transaction, stopwatch);
        var updateOriginalName = SaveName(connection, transaction, stopwatch);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE stopwatches
            SET name = CASE WHEN $updateName = 1 THEN $name ELSE name END,
                name_language = CASE WHEN $updateName = 1 AND $lang IS NOT NULL THEN $lang ELSE name_language END,
                color = $color, goal_seconds = $goal, shortcut = $shortcut,
                sort_order = $sort, is_deleted = $deleted, is_independent = $independent,
                is_permanent = $permanent, active_since_utc = $active, goal_since_utc = $goalSince,
                goal_per_cycle = $perCycle
            WHERE id = $id
            """;
        Bind(command, stopwatch);
        command.Parameters.AddWithValue("$updateName", updateOriginalName ? 1 : 0);
        command.Parameters.AddWithValue("$lang", (object?)Language ?? DBNull.Value);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static bool HasName(SqliteConnection connection, SqliteTransaction transaction, string id, string language)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM stopwatch_names WHERE stopwatch_id = $id AND language = $lang";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$lang", language);
        return command.ExecuteScalar() is not null;
    }

    /// <summary>
    /// Stores a name for another language when the stopwatch's original is in a different known language.
    /// Returns true when the name belongs in <c>stopwatches.name</c> instead.
    /// </summary>
    private bool SaveName(SqliteConnection connection, SqliteTransaction transaction, StopwatchDefinition stopwatch)
    {
        var language = Language;
        if (language is null)
        {
            return true;
        }

        string? originalLanguage;
        string originalName;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT name_language, name FROM stopwatches WHERE id = $id";
            read.Parameters.AddWithValue("$id", stopwatch.Id);
            using var reader = read.ExecuteReader();
            if (!reader.Read())
            {
                return true;
            }

            originalLanguage = reader.IsDBNull(0) ? null : reader.GetString(0);
            originalName = reader.GetString(1);
        }

        if (originalLanguage is null || originalLanguage == language)
        {
            return true;
        }

        // The name is just the fallback shown in this language (another field was saved): do not create a row for it.
        if (stopwatch.Name == originalName && !HasName(connection, transaction, stopwatch.Id, language))
        {
            return false;
        }

        using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = """
            INSERT INTO stopwatch_names (stopwatch_id, language, name) VALUES ($id, $lang, $name)
            ON CONFLICT(stopwatch_id, language) DO UPDATE SET name = excluded.name
            """;
        upsert.Parameters.AddWithValue("$id", stopwatch.Id);
        upsert.Parameters.AddWithValue("$lang", language);
        upsert.Parameters.AddWithValue("$name", stopwatch.Name);
        upsert.ExecuteNonQuery();
        return false;
    }

    public IReadOnlyList<GoalChange> GetGoalChanges()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT stopwatch_id, changed_utc, previous_goal_seconds FROM goal_changes ORDER BY changed_utc, id";
        var list = new List<GoalChange>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new GoalChange(
                reader.GetString(0),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                reader.IsDBNull(2) ? null : TimeSpan.FromSeconds(reader.GetInt64(2))));
        }

        return list;
    }

    public IReadOnlyList<GoalReset> GetGoalResets()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT stopwatch_id, reset_utc FROM goal_resets ORDER BY reset_utc, id";
        var list = new List<GoalReset>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new GoalReset(reader.GetString(0), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1))));
        }

        return list;
    }

    public void AddGoalReset(GoalReset reset)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO goal_resets (stopwatch_id, reset_utc) VALUES ($id, $reset)";
        command.Parameters.AddWithValue("$id", reset.StopwatchId);
        command.Parameters.AddWithValue("$reset", reset.ResetUtc.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    private void RecordGoalChange(SqliteConnection connection, SqliteTransaction transaction, StopwatchDefinition stopwatch)
    {
        long? previous;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT goal_seconds FROM stopwatches WHERE id = $id";
            read.Parameters.AddWithValue("$id", stopwatch.Id);
            using var reader = read.ExecuteReader();
            if (!reader.Read())
            {
                return;
            }

            previous = reader.IsDBNull(0) ? null : reader.GetInt64(0);
        }

        long? current = stopwatch.Goal is { } goal ? (long)goal.TotalSeconds : null;
        if (previous == current)
        {
            return;
        }

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO goal_changes (stopwatch_id, changed_utc, previous_goal_seconds) VALUES ($id, $changed, $previous)";
        insert.Parameters.AddWithValue("$id", stopwatch.Id);
        insert.Parameters.AddWithValue("$changed", (time ?? TimeProvider.System).GetUtcNow().ToUnixTimeMilliseconds());
        insert.Parameters.AddWithValue("$previous", previous is { } p ? (object)p : DBNull.Value);
        insert.ExecuteNonQuery();
    }

    public void SoftDelete(string id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE stopwatches SET is_deleted = 1 WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void SaveSortOrder(IReadOnlyList<string> orderedIds)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        for (var i = 0; i < orderedIds.Count; i++)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE stopwatches SET sort_order = $sort WHERE id = $id";
            command.Parameters.AddWithValue("$sort", i);
            command.Parameters.AddWithValue("$id", orderedIds[i]);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void Bind(SqliteCommand command, StopwatchDefinition s)
    {
        command.Parameters.AddWithValue("$id", s.Id);
        command.Parameters.AddWithValue("$name", s.Name);
        command.Parameters.AddWithValue("$color", s.Color);
        command.Parameters.AddWithValue("$goal", s.Goal is { } goal ? (object)(long)goal.TotalSeconds : DBNull.Value);
        command.Parameters.AddWithValue("$shortcut", (object?)s.Shortcut ?? DBNull.Value);
        command.Parameters.AddWithValue("$sort", s.SortOrder);
        command.Parameters.AddWithValue("$deleted", s.IsDeleted ? 1 : 0);
        command.Parameters.AddWithValue("$independent", s.IsIndependent ? 1 : 0);
        command.Parameters.AddWithValue("$permanent", s.IsPermanent ? 1 : 0);
        command.Parameters.AddWithValue("$active", s.ActiveSinceUtc is { } active ? (object)active.ToUnixTimeMilliseconds() : DBNull.Value);
        command.Parameters.AddWithValue("$goalSince", s.GoalSinceUtc is { } goalSince ? (object)goalSince.ToUnixTimeMilliseconds() : DBNull.Value);
        command.Parameters.AddWithValue("$perCycle", s.GoalPerCycle ? 1 : 0);
    }

    private static List<StopwatchDefinition> ReadAll(SqliteCommand command)
    {
        var list = new List<StopwatchDefinition>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new StopwatchDefinition(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : TimeSpan.FromSeconds(reader.GetInt64(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6) != 0,
                reader.GetInt32(7) != 0,
                reader.GetInt32(8) != 0,
                reader.IsDBNull(9) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(9)),
                reader.IsDBNull(10) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(10)),
                reader.GetInt32(11) != 0));
        }

        return list;
    }
}
