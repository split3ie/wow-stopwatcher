using Microsoft.Data.Sqlite;
using WowStopwatcher.Core;

namespace WowStopwatcher.Data;

/// <summary>SQLite implementation of <see cref="IStopwatchRepository"/>.</summary>
public sealed class SqliteStopwatchRepository(SqliteDatabase database) : IStopwatchRepository
{
    private const string Columns = "id, name, color, goal_seconds, shortcut, sort_order, is_deleted, is_independent";

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
            INSERT INTO stopwatches (id, name, color, goal_seconds, shortcut, sort_order, is_deleted, is_independent)
            VALUES ($id, $name, $color, $goal, $shortcut, $sort, $deleted, $independent)
            """;
        Bind(command, stopwatch);
        command.ExecuteNonQuery();
    }

    public void Update(StopwatchDefinition stopwatch)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE stopwatches
            SET name = $name, color = $color, goal_seconds = $goal, shortcut = $shortcut,
                sort_order = $sort, is_deleted = $deleted, is_independent = $independent
            WHERE id = $id
            """;
        Bind(command, stopwatch);
        command.ExecuteNonQuery();
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
                reader.GetInt32(7) != 0));
        }

        return list;
    }
}
