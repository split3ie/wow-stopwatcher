using Microsoft.Extensions.Time.Testing;
using WowStopwatcher.App.Features.Invoicing;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Tests;
using WowStopwatcher.Data;
using static WowStopwatcher.App.Tests.Invoicing.DocxTestFactory;

namespace WowStopwatcher.App.Tests.Invoicing;

public sealed class InvoicingServiceTests : IDisposable
{
    private const string CompanyId = "c1";
    private static readonly InvoicePeriod November = new(2026, 11);
    private static readonly DateOnly Nov2 = new(2026, 11, 2);
    private static readonly DateOnly Nov3 = new(2026, 11, 3);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeCompanyRepository _companies = new();
    private readonly FakeInvoiceRepository _invoices;
    private readonly FakeSessionRepository _sessions = new();
    private readonly FakeStopwatchRepository _stopwatches = new();
    private readonly InvoiceTemplateStore _store;
    private readonly InvoicingService _service;

    public InvoicingServiceTests()
    {
        Directory.CreateDirectory(_folder);
        _invoices = new FakeInvoiceRepository(_companies);
        _store = new InvoiceTemplateStore(new SqliteDatabase(Path.Combine(_folder, "db", "test.db")));

        _stopwatches.Items.Add(new StopwatchDefinition("a", "English", "#2F6FEB", null, null, 0));
        _stopwatches.Items.Add(new StopwatchDefinition("other", "Other", "#2F6FEB", null, null, 1));
        _companies.Companies.Add(new Company(CompanyId, "ACME", "#2F6FEB", 0, 60m, "EUR", null, null, null, "Invoice {{period.month}}", true, null, 5));
        _companies.Assigned[CompanyId] = ["a"];

        _sessions.Items.Add(new Session(1, "a", Utc(2026, 11, 2, 10), Utc(2026, 11, 2, 11)));
        _sessions.Items.Add(new Session(2, "a", Utc(2026, 11, 3, 9), Utc(2026, 11, 3, 9, 30)));
        _sessions.Items.Add(new Session(3, "a", Utc(2026, 10, 5, 9), Utc(2026, 10, 5, 10)));
        _sessions.Items.Add(new Session(4, "other", Utc(2026, 9, 5, 9), Utc(2026, 9, 5, 10)));

        _service = CreateService(new StubConverter(available: true));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static DateTimeOffset Utc(int y, int m, int d, int h, int min = 0) => new(y, m, d, h, min, 0, TimeSpan.Zero);

    private InvoicingService CreateService(IPdfConverter converter)
    {
        var time = new FakeTimeProvider(Utc(2026, 11, 15, 12));
        var selector = new PdfConverterSelector([converter]);
        return new InvoicingService(
            _companies,
            _invoices,
            _sessions,
            _stopwatches,
            new JintScriptEvaluator(),
            new DocxTemplateScanner(),
            new InvoicePdfBuilder(new DocxTemplateFiller(), selector),
            _store,
            new PdfPreviewRenderer(),
            selector,
            time);
    }

    private void UseTemplate(string body)
    {
        var source = Path.Combine(_folder, "template.docx");
        Create(source, [Para(TextRun(body))]);
        var stored = _store.Import(CompanyId, source);
        _companies.Update(_companies.Get(CompanyId)! with { TemplateFile = stored.FileName, TemplateName = stored.OriginalName, TemplateSha256 = stored.Sha256 });
    }

    private void Allocate(DateOnly date) =>
        _service.EditDay(CompanyId, November, date, lines => InvoiceLineEditor.SetNote(lines, 0, "work"));

    [Fact]
    public void Months_list_has_billed_months_and_invoice_rows_newest_first()
    {
        _invoices.GetOrCreate(CompanyId, new InvoicePeriod(2026, 8));

        var months = _service.GetMonths(CompanyId);

        Assert.Equal(["2026-11", "2026-10", "2026-08"], months.Select(m => m.Period.Key).ToArray());
        var november = months[0];
        Assert.Equal(0, november.InvoicedMinutes);
        Assert.Equal(90, november.UnallocatedMinutes);
        Assert.Equal(0m, november.Amount);
        Assert.True(november.IsCurrentMonth);
        Assert.Equal(InvoiceMonthStatus.Draft, november.Status);
        Assert.False(november.FileExists);
        Assert.False(months[1].IsCurrentMonth);
        Assert.Equal(0, months[2].InvoicedMinutes);

        Allocate(Nov2);
        var allocated = _service.GetMonths(CompanyId)[0];
        Assert.Equal(60, allocated.InvoicedMinutes);
        Assert.Equal(30, allocated.UnallocatedMinutes);
        Assert.Equal(60m, allocated.Amount);
        var summary = _service.GetSummary(CompanyId, November);
        Assert.Equal(new InvoiceSummary(60, 30, 90, 60, null, 60m, 60m, "EUR"), summary);
    }

    [Fact]
    public void Editing_a_day_stores_only_that_day()
    {
        var result = _service.EditDay(CompanyId, November, Nov2, lines => InvoiceLineEditor.Adjust(lines, 0, 45));

        Assert.Equal(EditDayResult.Saved, result);
        var stored = _invoices.GetStoredDays(_invoices.Get(CompanyId, November)!.Id);
        var only = Assert.Single(stored);
        Assert.Equal(Nov2, only.Date);
        Assert.True(only.Edited);
        var days = _service.GetDays(CompanyId, November);
        Assert.Equal(45, days[0].InvoicedMinutes);
        Assert.Equal(15, days[0].DriftMinutes);
        Assert.False(days[1].Edited);
        Assert.Equal(30, days[1].InvoicedMinutes);
    }

    [Fact]
    public void Apply_drift_adds_the_difference_to_the_last_line_and_reset_makes_the_day_live()
    {
        _service.EditDay(CompanyId, November, Nov2, lines => InvoiceLineEditor.Adjust(lines, 0, 45));

        Assert.Equal(EditDayResult.Saved, _service.ApplyDrift(CompanyId, November, Nov2));
        var day = _service.GetDays(CompanyId, November)[0];
        Assert.Equal(60, day.InvoicedMinutes);
        Assert.Equal(0, day.DriftMinutes);

        _service.EditDay(CompanyId, November, Nov2, lines => InvoiceLineEditor.Adjust(lines, 0, 10));
        Assert.Equal(EditDayResult.Saved, _service.ResetDay(CompanyId, November, Nov2));
        Assert.False(_service.GetDays(CompanyId, November)[0].Edited);
    }

    [Fact]
    public void Merging_days_moves_the_time_to_the_target_without_drift_and_keeps_the_emptied_day_edited()
    {
        var result = _service.MergeDays(CompanyId, November, [(Nov2, 0), (Nov3, 0)], Nov3);

        Assert.Equal(EditDayResult.Saved, result);
        var days = _service.GetDays(CompanyId, November);
        Assert.Empty(days[0].Lines);
        Assert.True(days[0].Edited);
        var merged = Assert.Single(days[1].Lines);
        Assert.Equal(90, merged.Minutes);
        Assert.Equal([new InvoiceLineMove(Nov2, 60)], merged.MovedIn);
        Assert.All(days, d => Assert.Equal(0, d.DriftMinutes));
        Assert.Equal(2, _invoices.GetStoredDays(_invoices.Get(CompanyId, November)!.Id).Count);
    }

    [Fact]
    public void Merging_days_is_refused_when_sent_or_when_days_have_a_gap()
    {
        _sessions.Items.Add(new Session(5, "a", Utc(2026, 11, 5, 9), Utc(2026, 11, 5, 9, 30)));

        Assert.Equal(EditDayResult.Unchanged, _service.MergeDays(CompanyId, November, [(Nov2, 0), (new DateOnly(2026, 11, 5), 0)], Nov2));
        Assert.Equal(EditDayResult.Unchanged, _service.MergeDays(CompanyId, November, [(Nov2, 0), (Nov3, 0)], new DateOnly(2026, 11, 5)));
        Assert.Equal(EditDayResult.Unchanged, _service.MergeDays(CompanyId, November, [(Nov2, 0)], Nov2));
        Assert.Null(_invoices.Get(CompanyId, November));

        Assert.Equal(MarkSentResult.Sent, _service.MarkSent(CompanyId, November, confirmChanged: false));

        Assert.Equal(EditDayResult.InvoiceSent, _service.MergeDays(CompanyId, November, [(Nov2, 0), (Nov3, 0)], Nov2));
    }

    [Fact]
    public void A_sent_invoice_rejects_edits_and_freezes_the_days()
    {
        Assert.Equal(MarkSentResult.Sent, _service.MarkSent(CompanyId, November, confirmChanged: false));

        var result = _service.EditDay(CompanyId, November, Nov2, lines => InvoiceLineEditor.Adjust(lines, 0, 45));

        Assert.Equal(EditDayResult.InvoiceSent, result);
        var invoice = _invoices.Get(CompanyId, November)!;
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.Equal(2, _invoices.GetStoredDays(invoice.Id).Count);
        Assert.All(_invoices.GetStoredDays(invoice.Id), d => Assert.False(d.Edited));
        Assert.Equal(MarkSentResult.AlreadySent, _service.MarkSent(CompanyId, November, confirmChanged: false));
        Assert.Equal(InvoiceMonthStatus.Sent, _service.GetMonths(CompanyId)[0].Status);
    }

    [Fact]
    public void A_sent_invoice_ignores_time_tracked_later()
    {
        _service.MarkSent(CompanyId, November, confirmChanged: false);
        _sessions.Items.Add(new Session(9, "a", Utc(2026, 11, 4, 9), Utc(2026, 11, 4, 10)));

        var days = _service.GetDays(CompanyId, November);

        Assert.Equal([Nov2, Nov3], days.Select(d => d.Date).ToArray());
    }

    [Fact]
    public void Mark_pending_makes_unedited_days_live_again()
    {
        _service.EditDay(CompanyId, November, Nov2, lines => InvoiceLineEditor.Adjust(lines, 0, 45));
        _service.MarkSent(CompanyId, November, confirmChanged: false);

        Assert.True(_service.MarkPending(CompanyId, November));

        var invoice = _invoices.Get(CompanyId, November)!;
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
        Assert.Equal([Nov2], _invoices.GetStoredDays(invoice.Id).Select(d => d.Date).ToArray());
        Assert.False(_service.MarkPending(CompanyId, November));
    }

    [Fact]
    public async Task Generating_shows_the_next_number_without_assigning_it()
    {
        UseTemplate("Invoice {{period.month}} {{hours}}");
        Allocate(Nov2);
        var destination = Path.Combine(_folder, "out", "gen.pdf");

        var generated = await _service.GenerateAsync(CompanyId, November, destination, CancellationToken.None);

        Assert.Equal(5, generated.InvoiceNumber);
        Assert.Null(_invoices.Get(CompanyId, November)!.Number);
        Assert.Equal(5, _companies.Get(CompanyId)!.NextInvoiceNumber);
    }

    [Fact]
    public void Mark_sent_assigns_the_number_and_pending_then_sent_keeps_it()
    {
        Assert.Equal(MarkSentResult.Sent, _service.MarkSent(CompanyId, November, confirmChanged: false));
        Assert.Equal(5, _invoices.Get(CompanyId, November)!.Number);
        Assert.Equal(6, _companies.Get(CompanyId)!.NextInvoiceNumber);

        Assert.True(_service.MarkPending(CompanyId, November));
        Assert.Equal(MarkSentResult.Sent, _service.MarkSent(CompanyId, November, confirmChanged: false));

        Assert.Equal(5, _invoices.Get(CompanyId, November)!.Number);
        Assert.Equal(6, _companies.Get(CompanyId)!.NextInvoiceNumber);
    }

    [Fact]
    public async Task Mark_sent_asks_when_the_input_changed_since_generation()
    {
        UseTemplate("Invoice {{period.month}} {{hours}}");
        Allocate(Nov2);
        var destination = Path.Combine(_folder, "out", "inv.pdf");
        var generated = await _service.GenerateAsync(CompanyId, November, destination, CancellationToken.None);
        Assert.Equal(GenerateOutcome.Generated, generated.Outcome);
        Assert.Equal(5, generated.InvoiceNumber);
        Assert.True(File.Exists(destination));

        // A change after generation.
        _service.EditDay(CompanyId, November, Nov2, lines => InvoiceLineEditor.Adjust(lines, 0, 45));
        Assert.Equal(MarkSentResult.ChangedSinceGeneration, _service.MarkSent(CompanyId, November, confirmChanged: false));
        Assert.Equal(InvoiceStatus.Pending, _invoices.Get(CompanyId, November)!.Status);

        Assert.Equal(MarkSentResult.Sent, _service.MarkSent(CompanyId, November, confirmChanged: true));
        var snapshot = _invoices.Get(CompanyId, November)!.SnapshotJson;
        Assert.Contains("\"Minutes\":45", snapshot);
        Assert.Contains("inv.pdf", snapshot);
    }

    [Fact]
    public async Task Mark_sent_needs_no_confirmation_when_nothing_changed()
    {
        UseTemplate("Invoice {{period.month}} {{hours}}");
        Allocate(Nov2);
        var destination = Path.Combine(_folder, "out", "inv.pdf");
        await _service.GenerateAsync(CompanyId, November, destination, CancellationToken.None);

        Assert.Equal(MarkSentResult.Sent, _service.MarkSent(CompanyId, November, confirmChanged: false));
    }

    [Fact]
    public async Task Generation_is_blocked_by_a_placeholder_error_used_in_the_template()
    {
        UseTemplate("Total {{bad}} {{good}}");
        _companies.Placeholders[CompanyId] =
        [
            new PlaceholderDefinition(1, CompanyId, "bad", PlaceholderKind.Script, "throw new Error('nope')", false, 0),
            new PlaceholderDefinition(2, CompanyId, "good", PlaceholderKind.Static, "fine", false, 1),
            new PlaceholderDefinition(3, CompanyId, "unused", PlaceholderKind.Script, "return (", false, 2),
        ];
        var destination = Path.Combine(_folder, "out", "inv.pdf");

        var result = await _service.GenerateAsync(CompanyId, November, destination, CancellationToken.None);

        Assert.Equal(GenerateOutcome.Blocked, result.Outcome);
        var block = Assert.Single(result.Blocks!);
        Assert.Equal(GenerateBlockKind.PlaceholderError, block.Kind);
        Assert.Equal("bad", block.Alias);
        Assert.Equal(ScriptErrorCode.Runtime, block.ErrorCode);
        Assert.False(File.Exists(destination));
        Assert.Null(_invoices.Get(CompanyId, November));
    }

    [Fact]
    public async Task Nothing_is_generated_when_no_line_has_a_note()
    {
        UseTemplate("Invoice {{hours}}");
        var destination = Path.Combine(_folder, "out", "inv.pdf");

        var result = await _service.GenerateAsync(CompanyId, November, destination, CancellationToken.None);

        Assert.Equal(GenerateOutcome.NothingAllocated, result.Outcome);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void Reported_time_replaces_the_billed_total_in_summary_and_month_row()
    {
        Allocate(Nov2);

        Assert.True(_service.SetReportedMinutes(CompanyId, November, 150));

        var summary = _service.GetSummary(CompanyId, November);
        Assert.Equal(new InvoiceSummary(60, 30, 90, 150, 150, 150m, 60m, "EUR"), summary);
        var row = _service.GetMonths(CompanyId)[0];
        Assert.Equal(150, row.InvoicedMinutes);
        Assert.Equal(150, row.ReportedMinutes);
        Assert.Equal(90, row.TrackedMinutes);
        Assert.Equal(150m, row.Amount);
        Assert.Equal(60, _service.GetSummary(CompanyId, November).BilledMinutes);

        Assert.True(_service.SetReportedMinutes(CompanyId, November, null));
        Assert.Equal(new InvoiceSummary(60, 30, 90, 60, null, 60m, 60m, "EUR"), _service.GetSummary(CompanyId, November));
    }

    [Fact]
    public void Reported_time_is_validated_and_clearing_without_a_row_does_nothing()
    {
        Assert.True(_service.SetReportedMinutes(CompanyId, November, null));
        Assert.Null(_invoices.Get(CompanyId, November));

        Assert.False(_service.SetReportedMinutes(CompanyId, November, 0));
        Assert.False(_service.SetReportedMinutes(CompanyId, November, InvoiceBilling.MaxReportedMinutes + 1));
        Assert.Null(_invoices.Get(CompanyId, November));

        Assert.True(_service.SetReportedMinutes(CompanyId, November, InvoiceBilling.MaxReportedMinutes));
        Assert.Equal(InvoiceBilling.MaxReportedMinutes, _invoices.Get(CompanyId, November)!.ReportedMinutes);
    }

    [Fact]
    public async Task Reported_time_allows_generation_without_noted_lines_and_feeds_the_values()
    {
        UseTemplate("Invoice {{hours}}");
        var destination = Path.Combine(_folder, "out", "inv.pdf");
        var before = await _service.GenerateAsync(CompanyId, November, destination, CancellationToken.None);
        Assert.Equal(GenerateOutcome.NothingAllocated, before.Outcome);

        _service.SetReportedMinutes(CompanyId, November, 120);
        var result = await _service.GenerateAsync(CompanyId, November, destination, CancellationToken.None);

        Assert.Equal(GenerateOutcome.Generated, result.Outcome);
        _companies.Update(_companies.Get(CompanyId)! with { FileNamePattern = "{{hours}}" });
        Assert.Equal("2.00.pdf", _service.SuggestFileName(CompanyId, November));
    }

    [Fact]
    public async Task Changing_the_reported_time_changes_the_input_hash()
    {
        UseTemplate("{{hours}}");
        _service.SetReportedMinutes(CompanyId, November, 120);
        await _service.GenerateAsync(CompanyId, November, Path.Combine(_folder, "o", "a.pdf"), CancellationToken.None);

        _service.SetReportedMinutes(CompanyId, November, 180);

        Assert.Equal(MarkSentResult.ChangedSinceGeneration, _service.MarkSent(CompanyId, November, confirmChanged: false));
    }

    [Fact]
    public void Reported_time_is_refused_when_sent_and_kept_after_mark_pending()
    {
        _service.SetReportedMinutes(CompanyId, November, 120);
        _service.MarkSent(CompanyId, November, confirmChanged: false);

        Assert.False(_service.SetReportedMinutes(CompanyId, November, 30));
        Assert.False(_service.SetReportedMinutes(CompanyId, November, null));
        Assert.Equal(120, _service.GetSummary(CompanyId, November).InvoicedMinutes);
        Assert.Equal(120, _service.GetMonths(CompanyId)[0].InvoicedMinutes);

        Assert.True(_service.MarkPending(CompanyId, November));
        Assert.Equal(120, _invoices.Get(CompanyId, November)!.ReportedMinutes);
        Assert.True(_service.SetReportedMinutes(CompanyId, November, 30));
    }

    [Fact]
    public async Task Only_noted_lines_reach_the_file_values_and_hash()
    {
        UseTemplate("{{hours}}");
        Allocate(Nov2);
        var withUnallocated = await _service.GenerateAsync(CompanyId, November, Path.Combine(_folder, "o", "a.pdf"), CancellationToken.None);
        Assert.Equal(GenerateOutcome.Generated, withUnallocated.Outcome);
        var hash = _invoices.Get(CompanyId, November)!.GeneratedInputHash;

        // More unallocated time does not change what is billed, so nothing counts as changed.
        _sessions.Items.Add(new Session(8, "a", Utc(2026, 11, 6, 9), Utc(2026, 11, 6, 10)));
        Assert.Equal(MarkSentResult.Sent, _service.MarkSent(CompanyId, November, confirmChanged: false));
        Assert.NotNull(hash);
    }

    [Fact]
    public async Task Generation_is_blocked_without_template_or_converter()
    {
        var noTemplate = await _service.GenerateAsync(CompanyId, November, Path.Combine(_folder, "a.pdf"), CancellationToken.None);
        Assert.Contains(noTemplate.Blocks!, b => b.Kind == GenerateBlockKind.NoTemplate);

        UseTemplate("x");
        var noConverter = await CreateService(new StubConverter(available: false))
            .GenerateAsync(CompanyId, November, Path.Combine(_folder, "a.pdf"), CancellationToken.None);
        Assert.Contains(noConverter.Blocks!, b => b.Kind == GenerateBlockKind.NoConverter);
    }

    [Fact]
    public void Template_warnings_list_unknown_markers_and_unused_placeholders()
    {
        UseTemplate("{{hours}} {{mystery}} {{used}}");
        _companies.Placeholders[CompanyId] =
        [
            new PlaceholderDefinition(1, CompanyId, "used", PlaceholderKind.Static, "x", false, 0),
            new PlaceholderDefinition(2, CompanyId, "spare", PlaceholderKind.Static, "y", false, 1),
        ];

        var warnings = _service.GetTemplateWarnings(CompanyId);

        Assert.Equal(["mystery"], warnings.UnknownMarkers);
        Assert.Equal(["spare"], warnings.UnusedPlaceholders);
    }

    [Fact]
    public void Template_warnings_include_file_name_markers_even_without_a_template()
    {
        _companies.Update(_companies.Get(CompanyId)! with { FileNamePattern = "{{period.month}}_{{used}}_{{mystery}}" });
        _companies.Placeholders[CompanyId] =
        [
            new PlaceholderDefinition(1, CompanyId, "used", PlaceholderKind.Static, "x", false, 0),
            new PlaceholderDefinition(2, CompanyId, "spare", PlaceholderKind.Static, "y", false, 1),
        ];

        var warnings = _service.GetTemplateWarnings(CompanyId);

        Assert.False(warnings.HasTemplate);
        Assert.Equal(["period.month", "used", "mystery"], warnings.Markers);
        Assert.Equal(["mystery"], warnings.UnknownMarkers);
        Assert.Equal(["spare"], warnings.UnusedPlaceholders);
    }

    [Fact]
    public void A_built_in_alias_with_a_static_row_overrides_the_value_but_not_the_summary()
    {
        Allocate(Nov2);
        _companies.Update(_companies.Get(CompanyId)! with { FileNamePattern = "{{company.name}}-{{hours}}-{{currency}}" });
        _companies.Placeholders[CompanyId] =
        [
            new PlaceholderDefinition(1, CompanyId, "company.name", PlaceholderKind.Static, "Override", false, 0),
            new PlaceholderDefinition(2, CompanyId, "currency", PlaceholderKind.Automatic, string.Empty, false, 1),
        ];

        Assert.Equal("Override-1.00-EUR.pdf", _service.SuggestFileName(CompanyId, November));
        Assert.Equal(new InvoiceSummary(60, 30, 90, 60, null, 60m, 60m, "EUR"), _service.GetSummary(CompanyId, November));
    }

    [Fact]
    public void An_automatic_row_shows_the_computed_built_in_value()
    {
        _companies.Placeholders[CompanyId] = [new PlaceholderDefinition(1, CompanyId, "currency", PlaceholderKind.Automatic, string.Empty, false, 0)];

        var result = _service.ResolvePlaceholdersForSample(CompanyId, 1m)["currency"];

        Assert.True(result.IsOk);
        Assert.Equal("EUR", result.Value);
    }

    [Fact]
    public void A_sent_invoice_freezes_an_overridden_built_in()
    {
        Allocate(Nov2);
        _companies.Update(_companies.Get(CompanyId)! with { FileNamePattern = "{{company.name}}-{{currency}}" });
        _companies.Placeholders[CompanyId] =
        [
            new PlaceholderDefinition(1, CompanyId, "company.name", PlaceholderKind.Static, "Frozen", false, 0),
            new PlaceholderDefinition(2, CompanyId, "currency", PlaceholderKind.Automatic, string.Empty, false, 1),
        ];

        Assert.Equal(MarkSentResult.Sent, _service.MarkSent(CompanyId, November, confirmChanged: false));
        var snapshot = _invoices.Get(CompanyId, November)!.SnapshotJson!;
        Assert.Contains("Frozen", snapshot);

        _companies.Placeholders[CompanyId] = [new PlaceholderDefinition(1, CompanyId, "company.name", PlaceholderKind.Static, "Changed", false, 0)];

        Assert.Equal("Frozen-EUR.pdf", _service.SuggestFileName(CompanyId, November));
    }

    [Fact]
    public void Suggested_file_name_uses_the_pattern_and_the_next_number()
    {
        _companies.Update(_companies.Get(CompanyId)! with { FileNamePattern = "{{company.name}}-{{invoice.number}}-{{period.month}}" });

        Assert.Equal("ACME-5-2026-11.pdf", _service.SuggestFileName(CompanyId, November));
    }

    [Fact]
    public void Sample_placeholders_use_the_sample_hours()
    {
        _companies.Placeholders[CompanyId] = [new PlaceholderDefinition(1, CompanyId, "total", PlaceholderKind.Script, "return hours + ' h'", false, 0)];

        var result = _service.ResolvePlaceholdersForSample(CompanyId, 2.5m);

        Assert.Equal("2.5 h", result["total"].Value);
    }

    [Fact]
    public void Hide_amounts_is_on_by_default_and_notifies_when_changed()
    {
        var settings = new InvoicingSettings(new FakeSettingsRepository());
        var raised = 0;
        settings.HideAmountsChanged += (_, _) => raised++;

        Assert.True(settings.HideAmounts);
        settings.HideAmounts = true;
        Assert.Equal(0, raised);
        settings.HideAmounts = false;

        Assert.False(settings.HideAmounts);
        Assert.Equal(1, raised);
    }

    private void UseNightShift()
    {
        _companies.Update(_companies.Get(CompanyId)! with { DayStartMinutes = 20 * 60 });

        // Shift 20:00 on Nov 9 to 04:00 on Nov 10, stored split at midnight.
        _sessions.Items.Add(new Session(10, "a", Utc(2026, 11, 9, 20), Utc(2026, 11, 10, 0)));
        _sessions.Items.Add(new Session(11, "a", Utc(2026, 11, 10, 0), Utc(2026, 11, 10, 4)));
    }

    [Fact]
    public void Day_start_joins_a_shift_split_at_midnight_into_one_day()
    {
        UseNightShift();

        var day = _service.GetDays(CompanyId, November).Single(d => d.Date == new DateOnly(2026, 11, 9));

        Assert.Equal(480, day.TrackedMinutes);
        Assert.DoesNotContain(_service.GetDays(CompanyId, November), d => d.Date == new DateOnly(2026, 11, 10));
        Assert.Equal(480, _service.GetStopwatchDay(CompanyId, November, new DateOnly(2026, 11, 9), "a").TrackedMinutes);
    }

    [Fact]
    public void Time_before_the_first_start_goes_to_the_previous_month_and_the_month_is_listed()
    {
        _companies.Update(_companies.Get(CompanyId)! with { DayStartMinutes = 20 * 60 });
        _sessions.Items.Add(new Session(10, "a", Utc(2026, 11, 1, 1), Utc(2026, 11, 1, 2)));

        var october = _service.GetDays(CompanyId, new InvoicePeriod(2026, 10)).Single(d => d.Date == new DateOnly(2026, 10, 31));

        Assert.Equal(60, october.TrackedMinutes);
    }

    [Fact]
    public void Running_stopwatch_names_use_the_invoice_day_range()
    {
        UseNightShift();
        _sessions.Items.Clear();
        _sessions.Items.Add(new Session(20, "a", Utc(2026, 11, 15, 8), null));  // now is Nov 15 12:00

        Assert.Equal(["English"], _service.GetRunningStopwatchNames(CompanyId, new DateOnly(2026, 11, 14)));
        Assert.Empty(_service.GetRunningStopwatchNames(CompanyId, new DateOnly(2026, 11, 15)));
    }

    private sealed class StubConverter(bool available) : IPdfConverter
    {
        public string Name => "Stub";

        public bool IsAvailable => available;

        public Task ConvertAsync(string docxPath, string outputPdfPath, CancellationToken cancellationToken)
        {
            File.WriteAllText(outputPdfPath, "%PDF-stub");
            return Task.CompletedTask;
        }
    }
}
