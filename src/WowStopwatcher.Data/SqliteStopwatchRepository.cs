using Microsoft.Data.Sqlite;
using WowStopwatcher.Core;

namespace WowStopwatcher.Data;

/// <summary>SQLite implementation of <see cref="IStopwatchRepository"/>.</summary>
public sealed class SqliteStopwatchRepository(SqliteDatabase database, TimeProvider? time = null) : IStopwatchRepository
{
    private const string Columns = "id, name, color, goal_seconds, shortcut, sort_order, is_deleted, is_independent, is_permanent, active_since_utc, goal_since_utc, goal_per_cycle";

    public IReadOnlyList<StopwatchDefinition> GetAll(bool includeDeleted = false)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM stopwatches"
            + (includeDeleted ? "" : " WHERE is_deleted = 0")
            + " ORDER BY sort_order, name, id";
        return ReadAll(command);
    }

    public StopwatchDefinition? Get(string id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM stopwatches WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return ReadAll(command).FirstOrDefault();
    }

    public void Add(StopwatchDefinition stopwatch)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO stopwatches (id, name, color, goal_seconds, shortcut, sort_order, is_deleted, is_independent, is_permanent, active_since_utc, goal_since_utc, goal_per_cycle)
            VALUES ($id, $name, $color, $goal, $shortcut, $sort, $deleted, $independent, $permanent, $active, $goalSince, $perCycle)
            """;
        Bind(command, stopwatch);
        command.ExecuteNonQuery();
    }

    public void Update(StopwatchDefinition stopwatch)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        RecordGoalChange(connection, transaction, stopwatch);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE stopwatches
            SET name = $name, color = $color, goal_seconds = $goal, shortcut = $shortcut,
                sort_order = $sort, is_deleted = $deleted, is_independent = $independent,
                is_permanent = $permanent, active_since_utc = $active, goal_since_utc = $goalSince,
                goal_per_cycle = $perCycle
            WHERE id = $id
            """;
        Bind(command, stopwatch);
        command.ExecuteNonQuery();
        transaction.Commit();
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
