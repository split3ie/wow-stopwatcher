using Microsoft.Extensions.Time.Testing;
using WowStopwatcher.App.Features.Invoicing;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Tests;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Tests.Invoicing;

public sealed class InvoiceTimeViewModelTests : IDisposable
{
    private const string CompanyId = "c1";
    private static readonly InvoicePeriod November = new(2026, 11);
    private static readonly DateOnly Nov2 = new(2026, 11, 2);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeCompanyRepository _companies = new();
    private readonly FakeInvoiceRepository _invoices;
    private readonly FakeSessionRepository _sessions = new();
    private readonly FakeStopwatchRepository _stopwatches = new();
    private readonly StopwatchDefinition _english = new("a", "English", "#2F6FEB", null, null, 0);
    private readonly InvoicingService _service;

    public InvoiceTimeViewModelTests()
    {
        Directory.CreateDirectory(_folder);
        _invoices = new FakeInvoiceRepository(_companies);
        _stopwatches.Items.Add(_english);
        _stopwatches.Items.Add(new StopwatchDefinition("other", "Other", "#2F6FEB", null, null, 1));
        _companies.Companies.Add(new Company(CompanyId, "ACME", "#2F6FEB", 0, 60m, "EUR", null, null, null, "Invoice", true, null, 5));
        _companies.Assigned[CompanyId] = ["a"];
        _sessions.Items.Add(new Session(1, "a", Utc(2026, 11, 2, 10), Utc(2026, 11, 2, 11)));

        var selector = new PdfConverterSelector([]);
        _service = new InvoicingService(
            _companies,
            _invoices,
            _sessions,
            _stopwatches,
            new JintScriptEvaluator(),
            new DocxTemplateScanner(),
            new InvoicePdfBuilder(new DocxTemplateFiller(), selector),
            new InvoiceTemplateStore(new SqliteDatabase(Path.Combine(_folder, "db", "test.db"))),
            new PdfPreviewRenderer(),
            selector,
            new FakeTimeProvider(Utc(2026, 11, 15, 12)));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static DateTimeOffset Utc(int y, int m, int d, int h, int min = 0) => new(y, m, d, h, min, 0, TimeSpan.Zero);

    private InvoiceTimeViewModel Create() => new(_service, CompanyId, "ACME", _english, Nov2, () => { });

    private IReadOnlyList<InvoiceLine> StoredLines() => _service.GetDays(CompanyId, November).Single(d => d.Date == Nov2).Lines;

    private static void Fill(InvoiceTimeViewModel vm, string time, string note)
    {
        vm.TimeInput = time;
        vm.NoteInput = note;
    }

    [Fact]
    public void Starts_with_all_tracked_time_unallocated()
    {
        var vm = Create();

        Assert.Equal("1:00", vm.TrackedText);
        Assert.Equal("0:00", vm.BilledText);
        Assert.Equal("1:00", vm.UnallocatedText);
        Assert.False(vm.IsOver);
        Assert.False(vm.IsSent);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public void Allocate_prefills_the_unallocated_time_and_needs_a_note()
    {
        var vm = Create();

        vm.StartAllocateCommand.Execute(null);

        Assert.True(vm.IsFormOpen);
        Assert.Equal("1:00", vm.TimeInput);
        Assert.Equal("Unallocated: 1:00", vm.Hint);
        Assert.False(vm.CanSave);

        vm.NoteInput = "   ";
        Assert.False(vm.CanSave);
        vm.NoteInput = "work";
        Assert.True(vm.CanSave);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0:60")]
    [InlineData("0")]
    public void Save_is_disabled_for_an_invalid_time(string time)
    {
        var vm = Create();
        vm.StartAllocateCommand.Execute(null);

        Fill(vm, time, "work");

        Assert.False(vm.CanSave);
    }

    [Theory]
    [InlineData("1", 60)]
    [InlineData("0:45", 45)]
    public void Saving_stores_the_entry_and_the_rest_stays_unallocated(string time, int minutes)
    {
        var vm = Create();
        vm.StartAllocateCommand.Execute(null);
        Fill(vm, time, " work ");

        vm.SaveCommand.Execute(null);

        Assert.False(vm.IsFormOpen);
        Assert.Equal(InvoiceTimeText.Format(minutes), vm.BilledText);
        Assert.Equal("work", Assert.Single(vm.Rows).Note);
        var lines = StoredLines();
        Assert.Equal(minutes, Assert.Single(lines, l => l.Note == "work").Minutes);
        Assert.Equal(60 - minutes, lines.Where(l => l.Note.Length == 0).Sum(l => l.Minutes));
    }

    [Fact]
    public void Time_beyond_the_tracked_time_shows_a_hint_and_is_billed_over_tracked()
    {
        var vm = Create();
        vm.StartAllocateCommand.Execute(null);

        Fill(vm, "1:30", "work");

        Assert.True(vm.IsHintOver);
        Assert.Equal("+0:30 more than tracked", vm.Hint);
        Assert.True(vm.CanSave);

        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsOver);
        Assert.Equal("+0:30 over tracked", vm.OverText);
        Assert.Equal("1:30", vm.BilledText);
        Assert.Equal("0:00", vm.UnallocatedText);
        Assert.All(StoredLines(), l => Assert.NotEqual(0, l.Note.Length));
    }

    [Fact]
    public void Edit_counts_the_entry_own_time_as_free_and_delete_returns_it_to_unallocated()
    {
        var vm = Create();
        vm.StartAllocateCommand.Execute(null);
        Fill(vm, "0:45", "first");
        vm.SaveCommand.Execute(null);

        vm.Rows[0].EditCommand.Execute(null);

        Assert.Equal("0:45", vm.TimeInput);
        Assert.Equal("first", vm.NoteInput);
        Assert.Equal("Unallocated: 1:00", vm.Hint);

        Fill(vm, "0:50", "second");
        vm.SaveCommand.Execute(null);
        Assert.Equal("second", Assert.Single(vm.Rows).Note);
        Assert.Equal("0:10", vm.UnallocatedText);

        vm.Rows[0].DeleteCommand.Execute(null);

        Assert.Empty(vm.Rows);
        Assert.Equal("1:00", vm.UnallocatedText);
        Assert.Equal("0:00", vm.BilledText);
    }

    [Fact]
    public void Cancel_closes_the_form_without_storing()
    {
        var vm = Create();
        vm.StartAllocateCommand.Execute(null);
        Fill(vm, "0:30", "work");

        vm.CancelCommand.Execute(null);

        Assert.False(vm.IsFormOpen);
        Assert.Empty(vm.Rows);
        Assert.Null(_invoices.Get(CompanyId, November));
    }

    [Fact]
    public void A_sent_invoice_is_read_only()
    {
        _invoices.MarkSent(_invoices.GetOrCreate(CompanyId, November).Id, Utc(2026, 11, 15, 12), "{}", []);

        var vm = Create();
        vm.StartAllocateCommand.Execute(null);

        Assert.True(vm.IsSent);
        Assert.False(vm.IsEditable);
        Assert.False(vm.CanAllocate);
        Assert.False(vm.IsFormOpen);
        Assert.Equal("Sent", vm.StatusText);
    }

    [Fact]
    public void Saving_after_the_invoice_was_sent_shows_an_error_and_changes_nothing()
    {
        var vm = Create();
        vm.StartAllocateCommand.Execute(null);
        Fill(vm, "0:30", "work");
        _invoices.MarkSent(_invoices.GetOrCreate(CompanyId, November).Id, Utc(2026, 11, 15, 12), "{}", []);

        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsSent);
        Assert.True(vm.HasError);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public void Merged_lines_with_the_stopwatch_are_listed_read_only()
    {
        _service.EditDay(CompanyId, November, Nov2, _ =>
        [
            new InvoiceLine(Nov2, 90, "both", [new InvoiceLineSource("a", "English"), new InvoiceLineSource("other", "Other")]),
        ]);

        var vm = Create();

        var merged = Assert.Single(vm.MergedRows);
        Assert.Equal("1:30", merged.TimeText);
        Assert.Equal("English + Other", merged.Sources);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public void The_statistics_action_applies_only_to_billed_stopwatches()
    {
        var action = new InvoiceTimeStatisticsAction(_companies, _stopwatches, _service, new NoWindowService(), new FakeNavigationService(), new InvoicingNavigationRequest());

        Assert.True(action.AppliesTo("a"));
        Assert.False(action.AppliesTo("other"));
        Assert.Equal("Invoice", action.Label);
    }

    private sealed class NoWindowService : IMainWindowService
    {
        public Avalonia.Controls.Window Owner => throw new NotSupportedException();

        public void Show()
        {
        }
    }
}
