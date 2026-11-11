using System.Globalization;
using Microsoft.Data.Sqlite;
using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Data;

/// <summary>SQLite implementation of <see cref="IInvoiceRepository"/>. Times are unix milliseconds (UTC); dates are yyyy-MM-dd.</summary>
public sealed class SqliteInvoiceRepository(SqliteDatabase database) : IInvoiceRepository
{
    private const string Columns = "id, company_id, year, month, status, number, file_path, generated_utc, generated_input_hash, sent_utc, snapshot_json, reported_minutes";

    private const string DateFormat = "yyyy-MM-dd";

    public IReadOnlyList<Invoice> GetForCompany(string companyId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM invoices WHERE company_id = $c ORDER BY year, month";
        command.Parameters.AddWithValue("$c", companyId);
        var result = new List<Invoice>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(Read(reader));
        }

        return result;
    }

    public Invoice? Get(string companyId, InvoicePeriod period)
    {
        using var connection = database.OpenConnection();
        return Find(connection, null, companyId, period);
    }

    public Invoice GetOrCreate(string companyId, InvoicePeriod period)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var existing = Find(connection, transaction, companyId, period);
        if (existing is not null)
        {
            return existing;
        }

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO invoices (company_id, year, month, status) VALUES ($c, $y, $m, 'pending')";
            insert.Parameters.AddWithValue("$c", companyId);
            insert.Parameters.AddWithValue("$y", period.Year);
            insert.Parameters.AddWithValue("$m", period.Month);
            insert.ExecuteNonQuery();
        }

        var created = Find(connection, transaction, companyId, period)!;
        transaction.Commit();
        return created;
    }

    public IReadOnlyList<StoredInvoiceDay> GetStoredDays(long invoiceId)
    {
        using var connection = database.OpenConnection();

        var days = new List<(string Date, bool Edited)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT local_date, edited FROM invoice_days WHERE invoice_id = $i ORDER BY local_date";
            command.Parameters.AddWithValue("$i", invoiceId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                days.Add((reader.GetString(0), reader.GetInt64(1) != 0));
            }
        }

        var sources = new Dictionary<long, List<InvoiceLineSource>>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT s.line_id, s.stopwatch_id, s.stopwatch_name FROM invoice_line_sources s
                JOIN invoice_lines l ON l.id = s.line_id WHERE l.invoice_id = $i ORDER BY s.line_id, s.sort_order
                """;
            command.Parameters.AddWithValue("$i", invoiceId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var lineId = reader.GetInt64(0);
                if (!sources.TryGetValue(lineId, out var list))
                {
                    sources[lineId] = list = [];
                }

                list.Add(new InvoiceLineSource(reader.GetString(1), reader.GetString(2)));
            }
        }

        var moves = new Dictionary<long, List<InvoiceLineMove>>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT m.line_id, m.from_date, m.minutes FROM invoice_line_moves m
                JOIN invoice_lines l ON l.id = m.line_id WHERE l.invoice_id = $i ORDER BY m.line_id, m.sort_order
                """;
            command.Parameters.AddWithValue("$i", invoiceId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var lineId = reader.GetInt64(0);
                if (!moves.TryGetValue(lineId, out var list))
                {
                    moves[lineId] = list = [];
                }

                list.Add(new InvoiceLineMove(ParseDate(reader.GetString(1)), reader.GetInt32(2)));
            }
        }

        var lines = new Dictionary<string, List<InvoiceLine>>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, local_date, minutes, note FROM invoice_lines WHERE invoice_id = $i ORDER BY local_date, sort_order, id";
            command.Parameters.AddWithValue("$i", invoiceId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var date = reader.GetString(1);
                if (!lines.TryGetValue(date, out var list))
                {
                    lines[date] = list = [];
                }

                IReadOnlyList<InvoiceLineSource> lineSources = sources.TryGetValue(reader.GetInt64(0), out var s) ? s : [];
                IReadOnlyList<InvoiceLineMove> lineMoves = moves.TryGetValue(reader.GetInt64(0), out var m) ? m : [];
                list.Add(new InvoiceLine(ParseDate(date), reader.GetInt32(2), reader.GetString(3), lineSources) { MovedIn = lineMoves });
            }
        }

        return days
            .Select(d => new StoredInvoiceDay(ParseDate(d.Date), d.Edited, lines.TryGetValue(d.Date, out var l) ? l : []))
            .ToList();
    }

    public void ReplaceDay(long invoiceId, StoredInvoiceDay day)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        WriteDay(connection, transaction, invoiceId, day);
        transaction.Commit();
    }

    public void ReplaceDays(long invoiceId, IReadOnlyList<StoredInvoiceDay> days)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var day in days)
        {
            WriteDay(connection, transaction, invoiceId, day);
        }

        transaction.Commit();
    }

    public void DeleteDay(long invoiceId, DateOnly date)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        RemoveDay(connection, transaction, invoiceId, date.ToString(DateFormat, CultureInfo.InvariantCulture));
        transaction.Commit();
    }

    public void MarkGenerated(long invoiceId, string filePath, DateTimeOffset generatedUtc, string inputHash)
    {
        using var connection = database.OpenConnection();
        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE invoices SET file_path = $path, generated_utc = $g, generated_input_hash = $h WHERE id = $i";
        update.Parameters.AddWithValue("$path", filePath);
        update.Parameters.AddWithValue("$g", generatedUtc.ToUnixTimeMilliseconds());
        update.Parameters.AddWithValue("$h", inputHash);
        update.Parameters.AddWithValue("$i", invoiceId);
        if (update.ExecuteNonQuery() == 0)
        {
            throw new InvalidOperationException($"Invoice {invoiceId} does not exist.");
        }
    }

    public void MarkSent(long invoiceId, DateTimeOffset sentUtc, string snapshotJson, IReadOnlyList<StoredInvoiceDay> daysToFreeze)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        int? number;
        string companyId;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT number, company_id FROM invoices WHERE id = $i";
            read.Parameters.AddWithValue("$i", invoiceId);
            using var reader = read.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException($"Invoice {invoiceId} does not exist.");
            }

            number = reader.IsDBNull(0) ? null : reader.GetInt32(0);
            companyId = reader.GetString(1);
        }

        if (number is null)
        {
            using (var next = connection.CreateCommand())
            {
                next.Transaction = transaction;
                next.CommandText = "SELECT next_invoice_number FROM companies WHERE id = $c";
                next.Parameters.AddWithValue("$c", companyId);
                number = Convert.ToInt32(next.ExecuteScalar());
            }

            using var bump = connection.CreateCommand();
            bump.Transaction = transaction;
            bump.CommandText = "UPDATE companies SET next_invoice_number = $n WHERE id = $c";
            bump.Parameters.AddWithValue("$n", number.Value + 1);
            bump.Parameters.AddWithValue("$c", companyId);
            bump.ExecuteNonQuery();
        }

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE invoices SET number = $n, status = 'sent', sent_utc = $s, snapshot_json = $j WHERE id = $i";
            update.Parameters.AddWithValue("$n", number.Value);
            update.Parameters.AddWithValue("$s", sentUtc.ToUnixTimeMilliseconds());
            update.Parameters.AddWithValue("$j", snapshotJson);
            update.Parameters.AddWithValue("$i", invoiceId);
            update.ExecuteNonQuery();
        }

        foreach (var day in daysToFreeze)
        {
            using var exists = connection.CreateCommand();
            exists.Transaction = transaction;
            exists.CommandText = "SELECT 1 FROM invoice_days WHERE invoice_id = $i AND local_date = $d";
            exists.Parameters.AddWithValue("$i", invoiceId);
            exists.Parameters.AddWithValue("$d", day.Date.ToString(DateFormat, CultureInfo.InvariantCulture));
            if (exists.ExecuteScalar() is not null)
            {
                continue;
            }

            WriteDay(connection, transaction, invoiceId, day with { Edited = false });
        }

        transaction.Commit();
    }

    public void MarkPending(long invoiceId)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var dates = new List<string>();
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT local_date FROM invoice_days WHERE invoice_id = $i AND edited = 0";
            select.Parameters.AddWithValue("$i", invoiceId);
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                dates.Add(reader.GetString(0));
            }
        }

        foreach (var date in dates)
        {
            RemoveDay(connection, transaction, invoiceId, date);
        }

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE invoices SET status = 'pending', sent_utc = NULL, snapshot_json = NULL WHERE id = $i";
            update.Parameters.AddWithValue("$i", invoiceId);
            update.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void SetReportedMinutes(long invoiceId, int? minutes)
    {
        using var connection = database.OpenConnection();
        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE invoices SET reported_minutes = $m WHERE id = $i";
        update.Parameters.AddWithValue("$m", minutes is null ? DBNull.Value : minutes.Value);
        update.Parameters.AddWithValue("$i", invoiceId);
        if (update.ExecuteNonQuery() == 0)
        {
            throw new InvalidOperationException($"Invoice {invoiceId} does not exist.");
        }
    }

    private static void WriteDay(SqliteConnection connection, SqliteTransaction transaction, long invoiceId, StoredInvoiceDay day)
    {
        var date = day.Date.ToString(DateFormat, CultureInfo.InvariantCulture);
        RemoveDay(connection, transaction, invoiceId, date);

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO invoice_days (invoice_id, local_date, edited) VALUES ($i, $d, $e)";
            insert.Parameters.AddWithValue("$i", invoiceId);
            insert.Parameters.AddWithValue("$d", date);
            insert.Parameters.AddWithValue("$e", day.Edited ? 1 : 0);
            insert.ExecuteNonQuery();
        }

        for (var order = 0; order < day.Lines.Count; order++)
        {
            var line = day.Lines[order];
            long lineId;
            using (var insertLine = connection.CreateCommand())
            {
                insertLine.Transaction = transaction;
                insertLine.CommandText = """
                    INSERT INTO invoice_lines (invoice_id, local_date, minutes, note, sort_order) VALUES ($i, $d, $m, $n, $o);
                    SELECT last_insert_rowid();
                    """;
                insertLine.Parameters.AddWithValue("$i", invoiceId);
                insertLine.Parameters.AddWithValue("$d", date);
                insertLine.Parameters.AddWithValue("$m", line.Minutes);
                insertLine.Parameters.AddWithValue("$n", line.Note);
                insertLine.Parameters.AddWithValue("$o", order);
                lineId = Convert.ToInt64(insertLine.ExecuteScalar());
            }

            for (var s = 0; s < line.Sources.Count; s++)
            {
                using var insertSource = connection.CreateCommand();
                insertSource.Transaction = transaction;
                insertSource.CommandText = "INSERT INTO invoice_line_sources (line_id, stopwatch_id, stopwatch_name, sort_order) VALUES ($l, $s, $n, $o)";
                insertSource.Parameters.AddWithValue("$l", lineId);
                insertSource.Parameters.AddWithValue("$s", line.Sources[s].StopwatchId);
                insertSource.Parameters.AddWithValue("$n", line.Sources[s].StopwatchName);
                insertSource.Parameters.AddWithValue("$o", s);
                insertSource.ExecuteNonQuery();
            }

            for (var m = 0; m < line.MovedIn.Count; m++)
            {
                using var insertMove = connection.CreateCommand();
                insertMove.Transaction = transaction;
                insertMove.CommandText = "INSERT INTO invoice_line_moves (line_id, from_date, minutes, sort_order) VALUES ($l, $f, $m, $o)";
                insertMove.Parameters.AddWithValue("$l", lineId);
                insertMove.Parameters.AddWithValue("$f", line.MovedIn[m].FromDate.ToString(DateFormat, CultureInfo.InvariantCulture));
                insertMove.Parameters.AddWithValue("$m", line.MovedIn[m].Minutes);
                insertMove.Parameters.AddWithValue("$o", m);
                insertMove.ExecuteNonQuery();
            }
        }
    }

    /// <summary>Deletes the day row and its lines; the sources go with the lines (foreign keys are enabled on every connection).</summary>
    private static void RemoveDay(SqliteConnection connection, SqliteTransaction transaction, long invoiceId, string date)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM invoice_lines WHERE invoice_id = $i AND local_date = $d;
            DELETE FROM invoice_days WHERE invoice_id = $i AND local_date = $d;
            """;
        command.Parameters.AddWithValue("$i", invoiceId);
        command.Parameters.AddWithValue("$d", date);
        command.ExecuteNonQuery();
    }

    private static Invoice? Find(SqliteConnection connection, SqliteTransaction? transaction, string companyId, InvoicePeriod period)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {Columns} FROM invoices WHERE company_id = $c AND year = $y AND month = $m";
        command.Parameters.AddWithValue("$c", companyId);
        command.Parameters.AddWithValue("$y", period.Year);
        command.Parameters.AddWithValue("$m", period.Month);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static Invoice Read(SqliteDataReader r) => new(
        r.GetInt64(0),
        r.GetString(1),
        new InvoicePeriod(r.GetInt32(2), r.GetInt32(3)),
        r.GetString(4) == "sent" ? InvoiceStatus.Sent : InvoiceStatus.Pending,
        r.IsDBNull(5) ? null : r.GetInt32(5),
        r.IsDBNull(6) ? null : r.GetString(6),
        r.IsDBNull(7) ? null : DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(7)),
        r.IsDBNull(8) ? null : r.GetString(8),
        r.IsDBNull(9) ? null : DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(9)),
        r.IsDBNull(10) ? null : r.GetString(10),
        r.IsDBNull(11) ? null : r.GetInt32(11));

    private static DateOnly ParseDate(string text) => DateOnly.ParseExact(text, DateFormat, CultureInfo.InvariantCulture);
}
