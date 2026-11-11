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
        Assert.Equal(new InvoiceSummary(60, 30, 60m, 60m, "EUR"), summary);
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
