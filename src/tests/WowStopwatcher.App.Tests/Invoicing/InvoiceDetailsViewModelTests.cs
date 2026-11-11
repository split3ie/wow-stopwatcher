using Microsoft.Extensions.Time.Testing;
using WowStopwatcher.App.Features.Invoicing;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Tests;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Tests.Invoicing;

public sealed class InvoiceDetailsViewModelTests : IDisposable
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
    private readonly InvoicingSettings _settings = new(new FakeSettingsRepository());
    private readonly FakeDialog _dialog = new();
    private readonly InvoicingService _service;
    private readonly FakeTimeProvider _time = new(Utc(2026, 11, 15, 12));

    public InvoiceDetailsViewModelTests()
    {
        Directory.CreateDirectory(_folder);
        _invoices = new FakeInvoiceRepository(_companies);
        _stopwatches.Items.Add(new StopwatchDefinition("a", "English", "#2F6FEB", null, null, 0));
        _stopwatches.Items.Add(new StopwatchDefinition("b", "Design", "#1F9D55", null, null, 1));
        _companies.Companies.Add(new Company(CompanyId, "ACME", "#2F6FEB", 0, 60m, "EUR", null, null, null, "Invoice", true, null, 5));
        _companies.Assigned[CompanyId] = ["a", "b"];

        // Nov 2: English 1:00, Design 0:30. Nov 3: English 0:30.
        _sessions.Items.Add(new Session(1, "a", Utc(2026, 11, 2, 10), Utc(2026, 11, 2, 11)));
        _sessions.Items.Add(new Session(2, "b", Utc(2026, 11, 2, 12), Utc(2026, 11, 2, 12, 30)));
        _sessions.Items.Add(new Session(3, "a", Utc(2026, 11, 3, 9), Utc(2026, 11, 3, 9, 30)));

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
            _time);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static DateTimeOffset Utc(int y, int m, int d, int h, int min = 0) => new(y, m, d, h, min, 0, TimeSpan.Zero);

    private InvoiceDetailsViewModel Create(Action? close = null) => new(
        _service,
        _companies,
        _stopwatches,
        _settings,
        _dialog,
        new ImmediateDispatcher(),
        _time,
        CompanyId,
        November,
        close ?? (() => { }));

    private IReadOnlyList<InvoiceLine> StoredLines(DateOnly date) => _service.GetDays(CompanyId, November).Single(d => d.Date == date).Lines;

    private bool IsEdited(DateOnly date) => _service.GetDays(CompanyId, November).Single(d => d.Date == date).Edited;

    private void MarkSent()
    {
        _service.EditDay(CompanyId, November, Nov2, lines => InvoiceLineEditor.SetNote(lines, 0, "Work"));
        Assert.Equal(MarkSentResult.Sent, _service.MarkSent(CompanyId, November, confirmChanged: false));
    }

    [Fact]
    public void Lines_are_grouped_by_day_and_unallocated_until_they_have_a_note()
    {
        var vm = Create();

        Assert.Equal(2, vm.Days.Count);
        Assert.Equal([Nov2, Nov3], vm.Days.Select(d => d.Date));
        Assert.Equal(["English", "Design"], vm.Days[0].Lines.Select(l => l.NameText));
        Assert.Equal(["1:00", "0:30"], vm.Days[0].Lines.Select(l => l.TimeText));
        Assert.All(vm.Days.SelectMany(d => d.Lines), l => Assert.True(l.IsUnallocated));
        Assert.Equal("ACME · November 2026", vm.Title);
        Assert.Equal("2h", vm.TrackedText);
        Assert.Equal("0m", vm.InvoicedText);
        Assert.Equal("2h", vm.UnallocatedText);
        Assert.False(vm.IsSent);
        Assert.Equal("Draft", vm.StatusText);
    }

    [Fact]
    public void A_note_makes_the_line_billed_and_is_saved()
    {
        var vm = Create();

        vm.Days[0].Lines[0].Note = "  Fixing bug ";
        vm.FlushNotes();

        Assert.True(vm.Days[0].Lines[0].IsBilled);
        Assert.Equal("1h", vm.InvoicedText);
        Assert.Equal("1h", vm.UnallocatedText);
        Assert.True(vm.Days[0].IsEdited);
        Assert.Equal("Fixing bug", StoredLines(Nov2)[0].Note);
        Assert.True(IsEdited(Nov2));
        Assert.False(IsEdited(Nov3));
    }

    [Fact]
    public void A_note_is_not_saved_before_the_delay_or_a_flush()
    {
        var vm = Create();

        vm.Days[0].Lines[0].Note = "Work";

        Assert.False(IsEdited(Nov2));

        vm.Dispose();

        Assert.True(IsEdited(Nov2));
    }

    [Fact]
    public void Split_needs_one_line_and_a_valid_first_part()
    {
        var vm = Create();
        vm.Days[0].Lines[0].IsSelected = true;
        vm.Days[0].Lines[1].IsSelected = true;
        Assert.False(vm.CanChangeOne);
        Assert.Equal("2 selected", vm.SelectionText);

        vm.Days[0].Lines[1].IsSelected = false;
        Assert.True(vm.CanChangeOne);
        vm.OpenSplitCommand.Execute(null);

        Assert.True(vm.IsFormOpen);
        Assert.Equal("00:30", vm.FormInput);
        Assert.Equal("Split 1:00", vm.FormTitle);
        Assert.Equal("Second part 0:30", vm.FormRest);
        Assert.False(vm.FormInvalid);

        foreach (var bad in new[] { "1:00", "0:00", "abc", "2:00", "0:61" })
        {
            vm.FormInput = bad;
            Assert.True(vm.FormInvalid, bad);
            Assert.Equal("Enter a time shorter than 1:00", vm.FormRest);
        }

        vm.ApplyFormCommand.Execute(null);
        Assert.False(IsEdited(Nov2));

        vm.FormInput = "0:20";
        Assert.Equal("Second part 0:40", vm.FormRest);
        vm.ApplyFormCommand.Execute(null);

        Assert.False(vm.IsFormOpen);
        Assert.Equal([20, 40, 30], StoredLines(Nov2).Select(l => l.Minutes));
        Assert.Equal(["English", "English", "Design"], StoredLines(Nov2).Select(l => l.Sources[0].StopwatchName));
        Assert.Equal(["0:20", "0:40", "0:30"], vm.Days[0].Lines.Select(l => l.TimeText));
        Assert.All(vm.Days.SelectMany(d => d.Lines), l => Assert.False(l.IsSelected));
    }

    [Fact]
    public void Adjust_sets_the_time_and_shows_the_tracked_time_of_the_day()
    {
        var vm = Create();
        vm.Days[0].Lines[0].IsSelected = true;

        vm.OpenAdjustCommand.Execute(null);

        Assert.Equal("01:00", vm.FormInput);
        Assert.Equal("Adjust 1:00", vm.FormTitle);
        Assert.Equal("Tracked for this day: 1:30", vm.FormRest);

        vm.FormInput = "0:00";
        Assert.True(vm.FormInvalid);
        Assert.Equal("Enter a time like 7:30", vm.FormRest);
        vm.ApplyFormCommand.Execute(null);
        Assert.False(IsEdited(Nov2));

        vm.FormInput = "0:45";
        vm.ApplyFormCommand.Execute(null);

        Assert.Equal([45, 30], StoredLines(Nov2).Select(l => l.Minutes));
        Assert.True(vm.Days[0].IsEdited);
    }

    [Fact]
    public void Changing_the_selection_closes_the_form()
    {
        var vm = Create();
        vm.Days[0].Lines[0].IsSelected = true;
        vm.OpenSplitCommand.Execute(null);
        Assert.True(vm.IsFormOpen);

        vm.CancelFormCommand.Execute(null);
        Assert.False(vm.IsFormOpen);

        vm.OpenAdjustCommand.Execute(null);
        vm.Days[0].Lines[1].IsSelected = true;

        Assert.False(vm.IsFormOpen);
    }

    [Fact]
    public void Merge_joins_lines_of_one_day()
    {
        var vm = Create();
        vm.Days[0].Lines[0].IsSelected = true;
        vm.Days[0].Lines[1].IsSelected = true;

        Assert.True(vm.CanMerge);
        vm.MergeCommand.Execute(null);

        var line = Assert.Single(StoredLines(Nov2));
        Assert.Equal(90, line.Minutes);
        Assert.Equal(["English", "Design"], line.Sources.Select(s => s.StopwatchName));
        var row = Assert.Single(vm.Days[0].Lines);
        Assert.True(row.IsMerged);
        Assert.Equal("Merged stopwatches", row.NameText);
        Assert.Equal("English + Design", row.PartsText);
        Assert.Equal("1:30", row.TimeText);
    }

    [Fact]
    public void Merge_across_neighbouring_days_asks_for_a_day_and_moves_the_time_without_drift()
    {
        var vm = Create();
        vm.Days[0].Lines[0].IsSelected = true;
        vm.Days[1].Lines[0].IsSelected = true;

        Assert.True(vm.CanMerge);
        vm.MergeCommand.Execute(null);

        Assert.True(vm.IsMergeDaysForm);
        Assert.False(vm.IsTimeForm);
        Assert.Equal(2, vm.MergeTargets.Count);
        Assert.False(vm.FormInvalid);

        vm.MergeTargetIndex = 0;
        vm.ApplyFormCommand.Execute(null);

        Assert.False(vm.IsFormOpen);
        var nov2 = _service.GetDays(CompanyId, November).Single(d => d.Date == Nov2);
        var nov3 = _service.GetDays(CompanyId, November).Single(d => d.Date == Nov3);
        Assert.Equal(2, nov2.Lines.Count);
        Assert.Equal(90, nov2.Lines[0].Minutes);
        Assert.Empty(nov3.Lines);
        Assert.True(nov3.Edited);
        Assert.Equal(0, nov2.DriftMinutes);
        Assert.Equal(0, nov3.DriftMinutes);
    }

    [Fact]
    public void Merge_across_days_with_a_gap_is_refused()
    {
        _sessions.Items.Add(new Session(4, "a", Utc(2026, 11, 5, 9), Utc(2026, 11, 5, 9, 30)));
        var vm = Create();
        vm.Days[0].Lines[0].IsSelected = true;
        vm.Days[2].Lines[0].IsSelected = true;

        Assert.False(vm.CanMerge);
        Assert.Equal("Merge works within one day or neighbouring days", vm.MergeHint);

        vm.MergeCommand.Execute(null);

        Assert.False(vm.IsFormOpen);
        Assert.False(IsEdited(Nov2));
        Assert.Equal(2, vm.Days[0].Lines.Count);
    }

    [Fact]
    public void Merge_needs_two_lines()
    {
        var vm = Create();
        vm.Days[0].Lines[0].IsSelected = true;

        Assert.False(vm.CanMerge);
        Assert.Equal("Select two or more lines of one day or neighbouring days to merge", vm.MergeHint);
    }

    [Fact]
    public void Add_time_adds_a_custom_line_to_a_day_without_tracked_time()
    {
        var vm = Create();

        vm.OpenAddCommand.Execute(null);
        Assert.True(vm.IsAddForm);
        Assert.True(vm.FormInvalid);
        vm.FormDayIndex = 4;
        vm.FormInput = "1:30";
        vm.FormNote = " Planning ";
        Assert.False(vm.FormInvalid);
        vm.ApplyFormCommand.Execute(null);

        var nov5 = new DateOnly(2026, 11, 5);
        var line = Assert.Single(StoredLines(nov5));
        Assert.True(line.IsCustom);
        Assert.Equal(90, line.Minutes);
        Assert.Equal("Planning", line.Note);
        Assert.True(IsEdited(nov5));
        Assert.False(vm.IsFormOpen);
        var row = vm.Days.Single(d => d.Date == nov5).Lines.Single();
        Assert.True(row.IsCustom);
        Assert.Equal("Custom", row.NameText);
        Assert.True(row.IsBilled);
        Assert.Equal("2h", vm.TrackedText);
        Assert.Equal("1h 30m", vm.InvoicedText);
    }

    [Fact]
    public void A_custom_line_does_not_cause_drift_and_is_added_at_the_end_of_a_tracked_day()
    {
        var vm = Create();
        vm.OpenAddCommand.Execute(null);
        vm.FormDayIndex = 1;
        vm.FormInput = "0:20";
        vm.ApplyFormCommand.Execute(null);

        var lines = StoredLines(Nov2);
        Assert.Equal(3, lines.Count);
        Assert.True(lines[2].IsCustom);
        Assert.Equal(0, _service.GetDays(CompanyId, November).Single(d => d.Date == Nov2).DriftMinutes);
        Assert.False(vm.Days[0].HasDrift);
    }

    [Fact]
    public void Add_time_needs_a_valid_time()
    {
        var vm = Create();
        vm.OpenAddCommand.Execute(null);

        vm.FormInput = "abc";
        Assert.True(vm.FormInvalid);
        vm.ApplyFormCommand.Execute(null);

        Assert.True(vm.IsAddForm);
        Assert.DoesNotContain(_service.GetDays(CompanyId, November), d => d.Edited);
    }

    [Fact]
    public void A_custom_line_can_be_adjusted_noted_and_deleted_but_not_merged()
    {
        var vm = Create();
        vm.OpenAddCommand.Execute(null);
        vm.FormDayIndex = 1;
        vm.FormInput = "0:20";
        vm.ApplyFormCommand.Execute(null);

        vm.Days[0].Lines[0].IsSelected = true;
        Assert.False(vm.CanDelete);
        vm.Days[0].Lines[2].IsSelected = true;
        Assert.False(vm.CanDelete);
        Assert.False(vm.CanMerge);
        Assert.Equal("A custom line cannot be merged", vm.MergeHint);
        vm.Days[0].Lines[0].IsSelected = false;

        Assert.True(vm.CanChangeOne);
        Assert.True(vm.CanDelete);
        vm.OpenAdjustCommand.Execute(null);
        vm.FormInput = "0:45";
        vm.ApplyFormCommand.Execute(null);
        Assert.Equal(45, StoredLines(Nov2)[2].Minutes);

        vm.Days[0].Lines[2].Note = "Call";
        vm.FlushNotes();
        Assert.Equal("Call", StoredLines(Nov2)[2].Note);

        vm.Days[0].Lines[2].IsSelected = true;
        vm.DeleteCustomCommand.Execute(null);
        Assert.Equal(2, StoredLines(Nov2).Count);
        Assert.All(StoredLines(Nov2), l => Assert.False(l.IsCustom));
    }

    [Fact]
    public void Deleting_the_last_line_of_a_custom_only_day_removes_the_day()
    {
        var vm = Create();
        vm.OpenAddCommand.Execute(null);
        vm.FormDayIndex = 6;
        vm.FormInput = "1:00";
        vm.ApplyFormCommand.Execute(null);
        var nov7 = new DateOnly(2026, 11, 7);

        vm.Days.Single(d => d.Date == nov7).Lines[0].IsSelected = true;
        vm.DeleteCustomCommand.Execute(null);

        Assert.DoesNotContain(_service.GetDays(CompanyId, November), d => d.Date == nov7);
        Assert.DoesNotContain(vm.Days, d => d.Date == nov7);
    }

    [Fact]
    public void Add_time_is_not_available_on_a_sent_invoice()
    {
        MarkSent();
        var vm = Create();

        vm.OpenAddCommand.Execute(null);

        Assert.False(vm.IsFormOpen);
        Assert.Equal(
            EditDayResult.InvoiceSent,
            _service.EditDay(CompanyId, November, Nov2, lines => InvoiceLineEditor.AddCustom(lines, Nov2, 30, "x")));
        Assert.DoesNotContain(StoredLines(Nov2), l => l.IsCustom);
        Assert.False(vm.CanDelete);
    }

    [Fact]
    public void Drift_is_shown_and_added_to_the_last_line()
    {
        _service.EditDay(CompanyId, November, Nov2, lines => InvoiceLineEditor.Adjust(lines, 1, 10));
        var vm = Create();

        var day = vm.Days[0];
        Assert.True(day.HasDrift);
        Assert.Equal("Tracked time changed: +20m", day.DriftText);

        day.ApplyDriftCommand.Execute(null);

        Assert.Equal([60, 30], StoredLines(Nov2).Select(l => l.Minutes));
        Assert.False(vm.Days[0].HasDrift);
    }

    [Fact]
    public void Reset_asks_first_and_returns_the_day_to_the_tracked_time()
    {
        _service.EditDay(CompanyId, November, Nov2, lines => InvoiceLineEditor.Adjust(lines, 0, 10));
        var vm = Create();
        Assert.True(vm.Days[0].CanReset);
        Assert.False(vm.Days[1].CanReset);

        vm.Days[0].ResetCommand.Execute(null);

        Assert.True(_dialog.IsOpen);
        Assert.True(IsEdited(Nov2));

        _dialog.Confirm();

        Assert.False(IsEdited(Nov2));
        Assert.False(vm.Days[0].IsEdited);
        Assert.Equal("1:00", vm.Days[0].Lines[0].TimeText);
    }

    [Fact]
    public void A_running_stopwatch_is_named_on_its_day()
    {
        _sessions.Items.Add(new Session(4, "a", Utc(2026, 11, 15, 10), null));
        var vm = Create();

        var day = vm.Days.Single(d => d.Date == new DateOnly(2026, 11, 15));

        Assert.True(day.IsRunning);
        Assert.Equal("English is running · counted up to now", day.RunningText);
        Assert.False(vm.Days[0].IsRunning);
    }

    [Fact]
    public void A_sent_invoice_is_read_only_until_marked_pending()
    {
        MarkSent();
        _settings.HideAmounts = false;
        var vm = Create();

        Assert.True(vm.IsSent);
        Assert.False(vm.IsEditable);
        Assert.Equal("Sent", vm.StatusText);
        Assert.Contains("Sent on 15 November 2026", vm.SentText);
        Assert.All(vm.Days.SelectMany(d => d.Lines), l => Assert.True(l.IsReadOnly));
        Assert.All(vm.Days.SelectMany(d => d.Lines), l => Assert.False(l.IsEditable));
        Assert.All(vm.Days, d => Assert.False(d.CanReset));

        vm.Days[0].Lines[1].IsSelected = true;
        Assert.False(vm.CanChangeOne);
        vm.OpenSplitCommand.Execute(null);
        Assert.False(vm.IsFormOpen);

        vm.Days[0].Lines[1].Note = "Changed";
        vm.FlushNotes();
        Assert.Equal(string.Empty, StoredLines(Nov2)[1].Note);

        vm.MarkPendingCommand.Execute(null);

        Assert.False(vm.IsSent);
        Assert.True(vm.IsEditable);
        Assert.Equal(InvoiceStatus.Pending, _invoices.Get(CompanyId, November)!.Status);
        Assert.All(vm.Days.SelectMany(d => d.Lines), l => Assert.True(l.IsEditable));
    }

    [Fact]
    public void A_sent_invoice_keeps_its_frozen_rate_and_currency()
    {
        MarkSent();
        _companies.Update(_companies.Get(CompanyId)! with { Rate = 100m, Currency = "GBP" });
        _settings.HideAmounts = false;

        var vm = Create();

        Assert.Equal("1h", vm.InvoicedText);
        Assert.Equal("60.00 EUR", vm.AmountText);
        Assert.Equal("Amount · 60 EUR/h", vm.AmountLabel);
        Assert.Equal("EUR", _service.GetMonths(CompanyId).Single(m => m.Period == November).Currency);
    }

    [Fact]
    public void The_months_list_uses_the_company_currency_until_the_invoice_is_sent()
    {
        _service.EditDay(CompanyId, November, Nov2, lines => InvoiceLineEditor.SetNote(lines, 0, "Work"));
        _companies.Update(_companies.Get(CompanyId)! with { Currency = "GBP" });

        Assert.Equal("GBP", _service.GetMonths(CompanyId).Single(m => m.Period == November).Currency);
    }

    [Fact]
    public void Amounts_are_masked_while_hidden_and_follow_the_toggle()
    {
        _service.EditDay(CompanyId, November, Nov2, lines => InvoiceLineEditor.SetNote(lines, 0, "Work"));
        _settings.HideAmounts = true;
        var vm = Create();

        Assert.True(vm.AmountsHidden);
        Assert.Equal("****", vm.AmountText);
        Assert.Equal("Amount · ****", vm.AmountLabel);
        Assert.Equal("Show amounts", vm.HideButtonText);

        vm.ToggleAmountsCommand.Execute(null);

        Assert.False(vm.AmountsHidden);
        Assert.Equal("60.00 EUR", vm.AmountText);
        Assert.Equal("Amount · 60 EUR/h", vm.AmountLabel);
        Assert.Equal("Hide amounts", vm.HideButtonText);
        Assert.False(_settings.HideAmounts);

        vm.Dispose();
        _settings.HideAmounts = true;
        Assert.False(vm.AmountsHidden);
    }

    [Fact]
    public void Back_saves_a_waiting_note_and_closes()
    {
        var closed = false;
        var vm = Create(() => closed = true);
        vm.Days[0].Lines[0].Note = "Work";

        vm.BackCommand.Execute(null);

        Assert.True(closed);
        Assert.True(IsEdited(Nov2));
    }

    [Fact]
    public void The_time_dialog_can_open_the_whole_invoice()
    {
        var opened = false;
        var english = new StopwatchDefinition("a", "English", "#2F6FEB", null, null, 0);
        var plain = new InvoiceTimeViewModel(_service, CompanyId, "ACME", english, Nov2, () => { });
        var linked = new InvoiceTimeViewModel(_service, CompanyId, "ACME", english, Nov2, () => { }, () => opened = true);

        Assert.False(plain.CanOpenInvoice);
        Assert.True(linked.CanOpenInvoice);

        linked.OpenInvoiceCommand.Execute(null);

        Assert.True(opened);
    }

    [Fact]
    public void The_navigation_request_is_taken_once()
    {
        var request = new InvoicingNavigationRequest();
        Assert.Null(request.Take());

        request.Set(CompanyId, November);

        Assert.Equal((CompanyId, November), request.Take());
        Assert.Null(request.Take());
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();

        public bool CheckAccess() => true;
    }

    private sealed class FakeDialog : IDialogService
    {
        private Action? _onConfirm;

        public bool IsOpen { get; private set; }

        public string Body { get; set; } = string.Empty;

        public void Show(string title, string body, string confirmLabel, Action onConfirm)
        {
            IsOpen = true;
            Body = body;
            _onConfirm = onConfirm;
        }

        public void Cancel() => IsOpen = false;

        public void Confirm()
        {
            IsOpen = false;
            _onConfirm?.Invoke();
        }
    }

    [Fact]
    public void Saving_reported_hours_sets_the_invoiced_total_and_shows_the_difference()
    {
        var vm = Create();
        Assert.Equal(string.Empty, vm.ReportedInput);
        Assert.False(vm.HasReported);

        vm.ReportedInput = "2:30";
        Assert.True(vm.CanSaveReported);
        vm.SaveReportedCommand.Execute(null);

        Assert.Equal(150, _service.GetSummary(CompanyId, November).ReportedMinutes);
        Assert.Equal("2h 30m", vm.InvoicedText);
        Assert.True(vm.HasReported);
        Assert.True(vm.ShowReportedClear);
        Assert.Equal("+30m vs tracked", vm.ReportedDifferenceText);
    }

    [Fact]
    public void Reported_hours_below_tracked_show_a_minus_and_equal_hours_show_same()
    {
        var vm = Create();

        vm.ReportedInput = "1:30";
        vm.SaveReportedCommand.Execute(null);
        Assert.Equal("−30m vs tracked", vm.ReportedDifferenceText);

        vm.ReportedInput = "2";
        vm.SaveReportedCommand.Execute(null);
        Assert.Equal("Same as tracked", vm.ReportedDifferenceText);
    }

    [Fact]
    public void Invalid_reported_hours_are_rejected()
    {
        var vm = Create();

        foreach (var text in new[] { "abc", "0", "0:00", "1:75", "9999" })
        {
            vm.ReportedInput = text;
            Assert.True(vm.ReportedInvalid, text);
            Assert.False(vm.CanSaveReported, text);
            vm.SaveReportedCommand.Execute(null);
        }

        Assert.Null(_service.GetSummary(CompanyId, November).ReportedMinutes);
        Assert.False(vm.HasReported);
    }

    [Fact]
    public void Clearing_reported_hours_returns_to_the_noted_lines()
    {
        var vm = Create();
        vm.ReportedInput = "3";
        vm.SaveReportedCommand.Execute(null);
        Assert.Equal("3h", vm.InvoicedText);

        vm.ClearReportedCommand.Execute(null);

        Assert.Null(_service.GetSummary(CompanyId, November).ReportedMinutes);
        Assert.Equal("0m", vm.InvoicedText);
        Assert.False(vm.HasReported);
        Assert.False(vm.ShowReportedClear);
        Assert.Equal(string.Empty, vm.ReportedInput);
    }

    [Fact]
    public void Reported_hours_start_with_the_invoiced_total_and_follow_it_until_typed_over()
    {
        var vm = Create();
        Assert.Equal(string.Empty, vm.ReportedInput);

        vm.Days[0].Lines[0].Note = "Work";
        vm.FlushNotes();
        Assert.Equal(InvoiceTimeText.FormatPadded(60), vm.ReportedInput);
        Assert.False(vm.HasReported);

        vm.Days[0].Lines[1].Note = "Design";
        vm.FlushNotes();
        Assert.Equal(InvoiceTimeText.FormatPadded(90), vm.ReportedInput);

        // The prefilled value is not a change: saving is a no-op.
        Assert.False(vm.ReportedChanged);
        Assert.False(vm.CanSaveReported);
        vm.SaveReportedCommand.Execute(null);
        Assert.Null(_service.GetSummary(CompanyId, November).ReportedMinutes);

        // A different value can be saved; afterwards there is nothing left to save.
        vm.ReportedInput = InvoiceTimeText.FormatPadded(120);
        Assert.True(vm.CanSaveReported);
        vm.SaveReportedCommand.Execute(null);
        Assert.Equal(120, _service.GetSummary(CompanyId, November).ReportedMinutes);
        Assert.False(vm.CanSaveReported);

        // After Clear the box shows the total again.
        vm.ClearReportedCommand.Execute(null);
        Assert.Equal(InvoiceTimeText.FormatPadded(90), vm.ReportedInput);

        // A value typed by the user is not overwritten by later edits.
        vm.ReportedInput = "5";
        vm.Days[1].Lines[0].Note = "More";
        vm.FlushNotes();
        Assert.Equal("5", vm.ReportedInput);
    }

    [Fact]
    public void Reported_hours_are_read_only_when_the_invoice_is_sent()
    {
        _service.SetReportedMinutes(CompanyId, November, 150);
        MarkSent();
        var vm = Create();

        Assert.True(vm.IsSent);
        Assert.True(vm.HasReported);
        Assert.Equal("2:30", vm.ReportedValueText);
        Assert.False(vm.ShowReportedClear);
        Assert.False(vm.CanSaveReported);

        vm.ReportedInput = "5";
        vm.SaveReportedCommand.Execute(null);
        vm.ClearReportedCommand.Execute(null);

        Assert.Equal(150, _service.GetSummary(CompanyId, November).ReportedMinutes);
    }
}
