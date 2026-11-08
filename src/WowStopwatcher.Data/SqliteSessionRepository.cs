using Microsoft.Data.Sqlite;
using WowStopwatcher.Core;

namespace WowStopwatcher.Data;

/// <summary>SQLite implementation of <see cref="ISessionRepository"/>. Times are stored as unix milliseconds (UTC).</summary>
public sealed class SqliteSessionRepository(SqliteDatabase database) : ISessionRepository
{
    private const string Columns = "id, stopwatch_id, start_utc, stop_utc";

    public long InsertOpen(string stopwatchId, DateTimeOffset startUtc)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sessions (stopwatch_id, start_utc, stop_utc) VALUES ($sw, $start, NULL);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$sw", stopwatchId);
        command.Parameters.AddWithValue("$start", startUtc.ToUnixTimeMilliseconds());
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public void Close(long sessionId, DateTimeOffset stopUtc)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE sessions SET stop_utc = $stop WHERE id = $id";
        command.Parameters.AddWithValue("$stop", stopUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$id", sessionId);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<Session> GetOpen() =>
        Query($"SELECT {Columns} FROM sessions WHERE stop_utc IS NULL ORDER BY start_utc, id");

    public Session? GetLatest() =>
        Query($"SELECT {Columns} FROM sessions ORDER BY start_utc DESC, id DESC LIMIT 1").FirstOrDefault();

    public Session? GetLatestForStopwatches(IReadOnlyCollection<string> stopwatchIds)
    {
        if (stopwatchIds.Count == 0)
        {
            return null;
        }

        var names = stopwatchIds.Select((_, i) => "$id" + i).ToArray();
        var parameters = stopwatchIds.Select((id, i) => (names[i], (object)id)).ToArray();
        return Query(
            $"SELECT {Columns} FROM sessions WHERE stopwatch_id IN ({string.Join(", ", names)}) ORDER BY start_utc DESC, id DESC LIMIT 1",
            parameters).FirstOrDefault();
    }

    public IReadOnlyList<Session> GetInRange(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        Query(
            $"SELECT {Columns} FROM sessions WHERE start_utc >= $from AND start_utc < $to ORDER BY start_utc, id",
            ("$from", fromUtc.ToUnixTimeMilliseconds()),
            ("$to", toUtc.ToUnixTimeMilliseconds()));

    public IReadOnlyList<DateOnly> GetLocalDays(TimeZoneInfo zone)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT start_utc FROM sessions";
        var days = new HashSet<DateOnly>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            days.Add(LocalDays.ToLocalDate(DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)), zone));
        }

        return days.OrderByDescending(d => d).ToList();
    }

    public int DeleteForStopwatchInRange(string stopwatchId, DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        Execute(
            "DELETE FROM sessions WHERE stopwatch_id = $sw AND start_utc >= $from AND start_utc < $to",
            ("$sw", stopwatchId),
            ("$from", fromUtc.ToUnixTimeMilliseconds()),
            ("$to", toUtc.ToUnixTimeMilliseconds()));

    public int DeleteForLocalDays(IEnumerable<DateOnly> days, TimeZoneInfo zone)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var deleted = 0;
        foreach (var day in days.Distinct())
        {
            var (from, to) = LocalDays.DayRangeUtc(day, zone);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM sessions WHERE start_utc >= $from AND start_utc < $to";
            command.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());
            deleted += command.ExecuteNonQuery();
        }

        transaction.Commit();
        return deleted;
    }

    public int DeleteAll() => Execute("DELETE FROM sessions");

    private int Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command.ExecuteNonQuery();
    }

    private List<Session> Query(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var list = new List<Session>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Session(
                reader.GetInt64(0),
                reader.GetString(1),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)),
                reader.IsDBNull(3) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3))));
        }

        return list;
    }
}
