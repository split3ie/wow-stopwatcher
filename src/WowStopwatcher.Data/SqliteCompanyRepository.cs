using System.Globalization;
using Microsoft.Data.Sqlite;
using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Data;

/// <summary>SQLite implementation of <see cref="ICompanyRepository"/>. Times are unix milliseconds (UTC); the rate is invariant-culture text.</summary>
public sealed class SqliteCompanyRepository(SqliteDatabase database, TimeProvider? time = null) : ICompanyRepository
{
    private const string Columns = "id, name, color, sort_order, rate, currency, template_file, template_name, template_sha256, file_name_pattern, ask_destination, output_folder, next_invoice_number, deleted_utc";

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public IReadOnlyList<Company> GetAll(bool includeDeleted)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM companies"
            + (includeDeleted ? "" : " WHERE deleted_utc IS NULL")
            + " ORDER BY sort_order, name, id";
        var result = new List<Company>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(Read(reader));
        }

        return result;
    }

    public Company? Get(string id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM companies WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public void Insert(Company company)
    {
        var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO companies (id, name, color, sort_order, rate, currency, template_file, template_name, template_sha256,
                file_name_pattern, ask_destination, output_folder, next_invoice_number, created_utc, deleted_utc)
            VALUES ($id, $name, $color, $order, $rate, $currency, $tfile, $tname, $tsha, $pattern, $ask, $folder, $next, $created, $deleted)
            """;
        Bind(command, company);
        command.Parameters.AddWithValue("$created", now);
        command.Parameters.AddWithValue("$deleted", company.IsDeleted ? now : DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void Update(Company company)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE companies SET name = $name, color = $color, sort_order = $order, rate = $rate, currency = $currency,
                template_file = $tfile, template_name = $tname, template_sha256 = $tsha, file_name_pattern = $pattern,
                ask_destination = $ask, output_folder = $folder, next_invoice_number = $next
            WHERE id = $id
            """;
        Bind(command, company);
        command.ExecuteNonQuery();
    }

    public void SoftDelete(string id)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE companies SET deleted_utc = $now WHERE id = $id AND deleted_utc IS NULL;
            DELETE FROM company_stopwatches WHERE company_id = $id;
            """;
        command.Parameters.AddWithValue("$now", _time.GetUtcNow().ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public IReadOnlySet<string> GetAssignedStopwatchIds(string companyId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT stopwatch_id FROM company_stopwatches WHERE company_id = $c";
        command.Parameters.AddWithValue("$c", companyId);
        var result = new HashSet<string>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    public IReadOnlyDictionary<string, string> GetStopwatchOwners()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT stopwatch_id, company_id FROM company_stopwatches";
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }

    public void SetAssignedStopwatches(string companyId, IReadOnlyCollection<string> stopwatchIds)
    {
        var ids = stopwatchIds.Distinct(StringComparer.Ordinal).ToList();
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var id in ids)
        {
            using var check = connection.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = "SELECT company_id FROM company_stopwatches WHERE stopwatch_id = $s AND company_id <> $c";
            check.Parameters.AddWithValue("$s", id);
            check.Parameters.AddWithValue("$c", companyId);
            if (check.ExecuteScalar() is string owner)
            {
                throw new InvalidOperationException($"Stopwatch '{id}' already belongs to another company ('{owner}').");
            }
        }

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM company_stopwatches WHERE company_id = $c";
            delete.Parameters.AddWithValue("$c", companyId);
            delete.ExecuteNonQuery();
        }

        foreach (var id in ids)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO company_stopwatches (company_id, stopwatch_id) VALUES ($c, $s)";
            insert.Parameters.AddWithValue("$c", companyId);
            insert.Parameters.AddWithValue("$s", id);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<PlaceholderDefinition> GetPlaceholders(string companyId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, company_id, alias, kind, value, hidden, sort_order FROM invoice_placeholders WHERE company_id = $c ORDER BY sort_order, id";
        command.Parameters.AddWithValue("$c", companyId);
        var result = new List<PlaceholderDefinition>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new PlaceholderDefinition(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3) == "script" ? PlaceholderKind.Script : PlaceholderKind.Static,
                reader.GetString(4),
                reader.GetInt64(5) != 0,
                reader.GetInt32(6)));
        }

        return result;
    }

    public void SavePlaceholders(string companyId, IReadOnlyList<PlaceholderDefinition> placeholders)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM invoice_placeholders WHERE company_id = $c";
            delete.Parameters.AddWithValue("$c", companyId);
            delete.ExecuteNonQuery();
        }

        foreach (var p in placeholders)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO invoice_placeholders (company_id, alias, kind, value, hidden, sort_order) VALUES ($c, $alias, $kind, $value, $hidden, $order)";
            insert.Parameters.AddWithValue("$c", companyId);
            insert.Parameters.AddWithValue("$alias", p.Alias);
            insert.Parameters.AddWithValue("$kind", p.Kind == PlaceholderKind.Script ? "script" : "static");
            insert.Parameters.AddWithValue("$value", p.Value);
            insert.Parameters.AddWithValue("$hidden", p.Hidden ? 1 : 0);
            insert.Parameters.AddWithValue("$order", p.SortOrder);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void Bind(SqliteCommand command, Company c)
    {
        command.Parameters.AddWithValue("$id", c.Id);
        command.Parameters.AddWithValue("$name", c.Name);
        command.Parameters.AddWithValue("$color", c.Color);
        command.Parameters.AddWithValue("$order", c.SortOrder);
        command.Parameters.AddWithValue("$rate", c.Rate.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$currency", c.Currency);
        command.Parameters.AddWithValue("$tfile", (object?)c.TemplateFile ?? DBNull.Value);
        command.Parameters.AddWithValue("$tname", (object?)c.TemplateName ?? DBNull.Value);
        command.Parameters.AddWithValue("$tsha", (object?)c.TemplateSha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("$pattern", c.FileNamePattern);
        command.Parameters.AddWithValue("$ask", c.AskDestination ? 1 : 0);
        command.Parameters.AddWithValue("$folder", (object?)c.OutputFolder ?? DBNull.Value);
        command.Parameters.AddWithValue("$next", c.NextInvoiceNumber);
    }

    private static Company Read(SqliteDataReader r) => new(
        r.GetString(0),
        r.GetString(1),
        r.GetString(2),
        r.GetInt32(3),
        decimal.Parse(r.GetString(4), NumberStyles.Number, CultureInfo.InvariantCulture),
        r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6),
        r.IsDBNull(7) ? null : r.GetString(7),
        r.IsDBNull(8) ? null : r.GetString(8),
        r.GetString(9),
        r.GetInt64(10) != 0,
        r.IsDBNull(11) ? null : r.GetString(11),
        r.GetInt32(12),
        !r.IsDBNull(13));
}
