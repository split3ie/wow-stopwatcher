using Microsoft.Data.Sqlite;
using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Data.Tests;

public class InvoicingRepositoryTests
{
    private static Company NewCompany(string id, int order = 0, decimal rate = 12.5m) => new(
        id, id.ToUpperInvariant(), "#2F6FEB", order, rate, "EUR", "t/a.docx", "a.docx", "abc", "{{number}}-{{company}}", true, null, 1);

    private static StoredInvoiceDay Day(int d, bool edited, params InvoiceLine[] lines) =>
        new(new DateOnly(2026, 11, d), edited, lines);

    private static InvoiceLine Line(int d, int minutes, string note, params (string Id, string Name)[] sources) =>
        new(new DateOnly(2026, 11, d), minutes, note, sources.Select(s => new InvoiceLineSource(s.Id, s.Name)).ToList());

    private static (TempDatabase Db, SqliteCompanyRepository Companies, SqliteInvoiceRepository Invoices) Setup()
    {
        var db = new TempDatabase();
        return (db, new SqliteCompanyRepository(db.Database), new SqliteInvoiceRepository(db.Database));
    }

    [Fact]
    public void Migration_V7ToV8_KeepsDataAndCreatesTables()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "v7.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE stopwatches (
                        id TEXT PRIMARY KEY NOT NULL, name TEXT NOT NULL, color TEXT NOT NULL,
                        goal_seconds INTEGER NULL, shortcut TEXT NULL, sort_order INTEGER NOT NULL,
                        is_deleted INTEGER NOT NULL DEFAULT 0, is_independent INTEGER NOT NULL DEFAULT 0,
                        is_permanent INTEGER NOT NULL DEFAULT 0, active_since_utc INTEGER NULL,
                        goal_since_utc INTEGER NULL, goal_per_cycle INTEGER NOT NULL DEFAULT 0, name_language TEXT NULL);
                    CREATE TABLE sessions (
                        id INTEGER PRIMARY KEY AUTOINCREMENT, stopwatch_id TEXT NOT NULL REFERENCES stopwatches(id),
                        start_utc INTEGER NOT NULL, stop_utc INTEGER NULL);
                    CREATE TABLE settings (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL);
                    CREATE TABLE goal_changes (
                        id INTEGER PRIMARY KEY AUTOINCREMENT, stopwatch_id TEXT NOT NULL REFERENCES stopwatches(id),
                        changed_utc INTEGER NOT NULL, previous_goal_seconds INTEGER NULL);
                    CREATE TABLE stopwatch_names (
                        stopwatch_id TEXT NOT NULL REFERENCES stopwatches(id), language TEXT NOT NULL, name TEXT NOT NULL,
                        PRIMARY KEY (stopwatch_id, language));
                    INSERT INTO stopwatches VALUES ('a', 'English', '#2F6FEB', 18000, 'Ctrl+F1', 0, 0, 0, 1, 1000, NULL, 0, 'en');
                    INSERT INTO sessions (stopwatch_id, start_utc, stop_utc) VALUES ('a', 1000, 2000);
                    PRAGMA user_version = 7;
                    """;
                command.ExecuteNonQuery();
            }

            var database = new SqliteDatabase(path);

            Assert.Equal(8, database.GetSchemaVersion());
            Assert.NotNull(database.BackupPath);
            Assert.Equal("English", Assert.Single(new SqliteStopwatchRepository(database).GetAll()).Name);
            Assert.NotNull(new SqliteSessionRepository(database).GetLatest());

            using var check = database.OpenConnection();
            using var command2 = check.CreateCommand();
            command2.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('companies','company_stopwatches','invoice_placeholders','invoices','invoice_days','invoice_lines','invoice_line_sources')";
            Assert.Equal(7L, Convert.ToInt64(command2.ExecuteScalar()));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Company_RoundTripsAndOrdersBySortOrder()
    {
        var (db, companies, _) = Setup();
        using (db)
        {
            companies.Insert(NewCompany("b", 1, 99.95m));
            companies.Insert(NewCompany("a", 0) with { TemplateFile = null, TemplateName = null, TemplateSha256 = null, OutputFolder = "C:\\out", AskDestination = false });

            var all = companies.GetAll(includeDeleted: false);
            Assert.Equal(["a", "b"], all.Select(c => c.Id));
            Assert.Equal(99.95m, companies.Get("b")!.Rate);
            Assert.Equal(NewCompany("b", 1, 99.95m), companies.Get("b"));
            var a = companies.Get("a")!;
            Assert.Null(a.TemplateFile);
            Assert.False(a.AskDestination);
            Assert.Equal("C:\\out", a.OutputFolder);

            companies.Update(a with { Name = "Renamed", Rate = 7m, NextInvoiceNumber = 5 });
            Assert.Equal("Renamed", companies.Get("a")!.Name);
            Assert.Equal(5, companies.Get("a")!.NextInvoiceNumber);
            Assert.Null(companies.Get("missing"));
        }
    }

    [Fact]
    public void Company_SoftDelete_HidesFromDefaultListButKeepsData()
    {
        var (db, companies, _) = Setup();
        using (db)
        {
            companies.Insert(NewCompany("a"));
            companies.Insert(NewCompany("b", 1));

            companies.SoftDelete("a");

            Assert.Equal(["b"], companies.GetAll(includeDeleted: false).Select(c => c.Id));
            var withDeleted = companies.GetAll(includeDeleted: true);
            Assert.Equal(2, withDeleted.Count);
            Assert.True(withDeleted.Single(c => c.Id == "a").IsDeleted);
            Assert.True(companies.Get("a")!.IsDeleted);
        }
    }

    [Fact]
    public void Company_SoftDelete_ReleasesStopwatches()
    {
        var (db, companies, _) = Setup();
        using (db)
        {
            companies.Insert(NewCompany("a"));
            companies.Insert(NewCompany("b", 1));
            companies.SetAssignedStopwatches("a", ["s1", "s2"]);

            companies.SoftDelete("a");

            Assert.Empty(companies.GetStopwatchOwners());
            companies.SetAssignedStopwatches("b", ["s1"]);
            Assert.Equal("b", companies.GetStopwatchOwners()["s1"]);
        }
    }

    [Fact]
    public void SetAssignedStopwatches_ReplacesAndReportsOwners()
    {
        var (db, companies, _) = Setup();
        using (db)
        {
            companies.Insert(NewCompany("a"));
            companies.Insert(NewCompany("b", 1));

            companies.SetAssignedStopwatches("a", ["s1", "s2"]);
            companies.SetAssignedStopwatches("a", ["s2", "s3"]);
            companies.SetAssignedStopwatches("b", ["s1"]);

            Assert.Equal(new HashSet<string> { "s2", "s3" }, companies.GetAssignedStopwatchIds("a"));
            Assert.Equal(new HashSet<string> { "s1" }, companies.GetAssignedStopwatchIds("b"));
            var owners = companies.GetStopwatchOwners();
            Assert.Equal("a", owners["s2"]);
            Assert.Equal("b", owners["s1"]);
        }
    }

    [Fact]
    public void SetAssignedStopwatches_Conflict_ThrowsAndChangesNothing()
    {
        var (db, companies, _) = Setup();
        using (db)
        {
            companies.Insert(NewCompany("a"));
            companies.Insert(NewCompany("b", 1));
            companies.SetAssignedStopwatches("a", ["s1"]);
            companies.SetAssignedStopwatches("b", ["s2"]);

            var ex = Assert.Throws<InvalidOperationException>(() => companies.SetAssignedStopwatches("b", ["s3", "s1"]));

            Assert.Contains("s1", ex.Message);
            Assert.Equal(new HashSet<string> { "s2" }, companies.GetAssignedStopwatchIds("b"));
            Assert.Equal(new HashSet<string> { "s1" }, companies.GetAssignedStopwatchIds("a"));
        }
    }

    [Fact]
    public void Placeholders_RoundTrip_IncludingKindHiddenAndReplaceAll()
    {
        var (db, companies, _) = Setup();
        using (db)
        {
            companies.Insert(NewCompany("a"));
            companies.SavePlaceholders("a",
            [
                new PlaceholderDefinition(0, "a", "second", PlaceholderKind.Script, "return 1;", true, 1),
                new PlaceholderDefinition(0, "a", "first", PlaceholderKind.Static, "Hello", false, 0),
            ]);

            var saved = companies.GetPlaceholders("a");
            Assert.Equal(["first", "second"], saved.Select(p => p.Alias));
            Assert.Equal(PlaceholderKind.Static, saved[0].Kind);
            Assert.False(saved[0].Hidden);
            Assert.Equal("Hello", saved[0].Value);
            Assert.Equal(PlaceholderKind.Script, saved[1].Kind);
            Assert.True(saved[1].Hidden);
            Assert.All(saved, p => Assert.True(p.Id > 0));

            companies.SavePlaceholders("a", [new PlaceholderDefinition(0, "a", "only", PlaceholderKind.Static, "", false, 0)]);

            Assert.Equal("only", Assert.Single(companies.GetPlaceholders("a")).Alias);
        }
    }

    [Fact]
    public void GetOrCreate_IsIdempotent_AndGetReturnsNullBeforeCreation()
    {
        var (db, companies, invoices) = Setup();
        using (db)
        {
            companies.Insert(NewCompany("a"));
            var period = new InvoicePeriod(2026, 11);
            Assert.Null(invoices.Get("a", period));

            var first = invoices.GetOrCreate("a", period);
            var second = invoices.GetOrCreate("a", period);

            Assert.Equal(first.Id, second.Id);
            Assert.Equal(InvoiceStatus.Pending, first.Status);
            Assert.Null(first.Number);
            Assert.Equal(first, invoices.Get("a", period));
            Assert.Single(invoices.GetForCompany("a"));
        }
    }

    [Fact]
    public void ReplaceDay_RoundTripsMergedSourcesAndNotes_OrderedByDate()
    {
        var (db, companies, invoices) = Setup();
        using (db)
        {
            companies.Insert(NewCompany("a"));
            var invoice = invoices.GetOrCreate("a", new InvoicePeriod(2026, 11));

            invoices.ReplaceDay(invoice.Id, Day(5, true, Line(5, 90, "merged", ("s2", "Two"), ("s1", "One")), Line(5, 30, "")));
            invoices.ReplaceDay(invoice.Id, Day(2, false, Line(2, 15, "x", ("s1", "One"))));

            var days = invoices.GetStoredDays(invoice.Id);
            Assert.Equal([new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 5)], days.Select(d => d.Date));
            Assert.False(days[0].Edited);
            Assert.True(days[1].Edited);
            var lines = days[1].Lines;
            Assert.Equal(2, lines.Count);
            Assert.Equal(90, lines[0].Minutes);
            Assert.Equal("merged", lines[0].Note);
            Assert.Equal(["s2", "s1"], lines[0].Sources.Select(s => s.StopwatchId));
            Assert.Equal(["Two", "One"], lines[0].Sources.Select(s => s.StopwatchName));
            Assert.True(lines[0].IsMerged);
            Assert.Equal(30, lines[1].Minutes);
            Assert.Empty(lines[1].Sources);

            // Replacing a day replaces its lines.
            invoices.ReplaceDay(invoice.Id, Day(5, true, Line(5, 10, "new", ("s1", "One"))));
            var replaced = invoices.GetStoredDays(invoice.Id).Single(d => d.Date.Day == 5);
            Assert.Equal(10, Assert.Single(replaced.Lines).Minutes);
        }
    }

    [Fact]
    public void ReplaceDay_WithNoLines_StoresAnEmptyDay()
    {
        var (db, companies, invoices) = Setup();
        using (db)
        {
            companies.Insert(NewCompany("a"));
            var invoice = invoices.GetOrCreate("a", new InvoicePeriod(2026, 11));

            invoices.ReplaceDay(invoice.Id, Day(3, true));

            var day = Assert.Single(invoices.GetStoredDays(invoice.Id));
            Assert.Empty(day.Lines);
        }
    }

    [Fact]
    public void DeleteDay_RemovesOnlyThatDay()
    {
        var (db, companies, invoices) = Setup();
        using (db)
        {
            companies.Insert(NewCompany("a"));
            var invoice = invoices.GetOrCreate("a", new InvoicePeriod(2026, 11));
            invoices.ReplaceDay(invoice.Id, Day(1, true, Line(1, 10, "", ("s1", "One"))));
            invoices.ReplaceDay(invoice.Id, Day(2, true, Line(2, 20, "", ("s1", "One"))));

            invoices.DeleteDay(invoice.Id, new DateOnly(2026, 11, 1));

            var day = Assert.Single(invoices.GetStoredDays(invoice.Id));
            Assert.Equal(2, day.Date.Day);
            Assert.Equal(20, Assert.Single(day.Lines).Minutes);
        }
    }

    [Fact]
    public void MarkGenerated_AssignsNumbersOncePerInvoice()
    {
        var (db, companies, invoices) = Setup();
        using (db)
        {
            companies.Insert(NewCompany("a") with { NextInvoiceNumber = 7 });
            var nov = invoices.GetOrCreate("a", new InvoicePeriod(2026, 11));
            var dec = invoices.GetOrCreate("a", new InvoicePeriod(2026, 12));
            var when = new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);

            Assert.Equal(7, invoices.MarkGenerated(nov.Id, "C:\\a.pdf", when, "h1"));
            Assert.Equal(8, companies.Get("a")!.NextInvoiceNumber);

            Assert.Equal(7, invoices.MarkGenerated(nov.Id, "C:\\b.pdf", when.AddHours(1), "h2"));
            Assert.Equal(8, companies.Get("a")!.NextInvoiceNumber);

            Assert.Equal(8, invoices.MarkGenerated(dec.Id, "C:\\c.pdf", when, "h3"));
            Assert.Equal(9, companies.Get("a")!.NextInvoiceNumber);

            var stored = invoices.Get("a", new InvoicePeriod(2026, 11))!;
            Assert.Equal(7, stored.Number);
            Assert.Equal("C:\\b.pdf", stored.FilePath);
            Assert.Equal("h2", stored.GeneratedInputHash);
            Assert.Equal(when.AddHours(1), stored.GeneratedUtc);
        }
    }

    [Fact]
    public void MarkSent_FreezesGivenDays_WithoutOverwritingEditedOnes()
    {
        var (db, companies, invoices) = Setup();
        using (db)
        {
            companies.Insert(NewCompany("a"));
            var invoice = invoices.GetOrCreate("a", new InvoicePeriod(2026, 11));
            invoices.ReplaceDay(invoice.Id, Day(1, true, Line(1, 10, "mine", ("s1", "One"))));
            var sent = new DateTimeOffset(2026, 12, 2, 8, 0, 0, TimeSpan.Zero);

            invoices.MarkSent(invoice.Id, sent, "{\"x\":1}",
            [
                Day(1, true, Line(1, 999, "live", ("s1", "One"))),
                Day(2, true, Line(2, 20, "", ("s1", "One"))),
            ]);

            var after = invoices.Get("a", new InvoicePeriod(2026, 11))!;
            Assert.Equal(InvoiceStatus.Sent, after.Status);
            Assert.Equal(sent, after.SentUtc);
            Assert.Equal("{\"x\":1}", after.SnapshotJson);
            var days = invoices.GetStoredDays(invoice.Id);
            Assert.Equal(2, days.Count);
            Assert.True(days[0].Edited);
            Assert.Equal(10, Assert.Single(days[0].Lines).Minutes);
            Assert.Equal("mine", days[0].Lines[0].Note);
            Assert.False(days[1].Edited);
            Assert.Equal(20, Assert.Single(days[1].Lines).Minutes);
        }
    }

    [Fact]
    public void MarkPending_RemovesOnlyNonEditedDaysAndClearsSentData()
    {
        var (db, companies, invoices) = Setup();
        using (db)
        {
            companies.Insert(NewCompany("a"));
            var invoice = invoices.GetOrCreate("a", new InvoicePeriod(2026, 11));
            invoices.ReplaceDay(invoice.Id, Day(1, true, Line(1, 10, "mine", ("s1", "One"))));
            invoices.MarkSent(invoice.Id, DateTimeOffset.UtcNow, "{}", [Day(2, false, Line(2, 20, "", ("s1", "One")))]);

            invoices.MarkPending(invoice.Id);

            var after = invoices.Get("a", new InvoicePeriod(2026, 11))!;
            Assert.Equal(InvoiceStatus.Pending, after.Status);
            Assert.Null(after.SentUtc);
            Assert.Null(after.SnapshotJson);
            var day = Assert.Single(invoices.GetStoredDays(invoice.Id));
            Assert.True(day.Edited);
            Assert.Equal(1, day.Date.Day);
            Assert.Equal("mine", Assert.Single(day.Lines).Note);
        }
    }
}
