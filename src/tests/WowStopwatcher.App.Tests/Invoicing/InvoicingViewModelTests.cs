using Microsoft.Extensions.Time.Testing;
using WowStopwatcher.App.Features.Invoicing;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Tests;
using WowStopwatcher.Data;
using static WowStopwatcher.App.Tests.Invoicing.DocxTestFactory;

namespace WowStopwatcher.App.Tests.Invoicing;

public sealed class InvoicingViewModelTests : IDisposable
{
    private static readonly InvoicePeriod November = new(2026, 11);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeCompanyRepository _companies = new();
    private readonly FakeInvoiceRepository _invoices;
    private readonly FakeSessionRepository _sessions = new();
    private readonly FakeStopwatchRepository _stopwatches = new();
    private readonly FakeSettingsRepository _settingsStore = new();
    private readonly FakeDialog _dialog = new();
    private readonly FakePicker _picker = new();
    private readonly FakeClipboard _clipboard = new();
    private readonly FakeLauncher _launcher = new();
    private readonly InvoiceTemplateStore _store;
    private readonly InvoicingService _service;
    private readonly InvoicingSettings _settings;
    private readonly InvoicingNavigationRequest _request = new();

    public InvoicingViewModelTests()
    {
        Directory.CreateDirectory(_folder);
        _invoices = new FakeInvoiceRepository(_companies);
        _store = new InvoiceTemplateStore(new SqliteDatabase(Path.Combine(_folder, "db", "test.db")));
        _settings = new InvoicingSettings(_settingsStore);

        _stopwatches.Items.Add(new StopwatchDefinition("a", "English", "#2F6FEB", null, null, 0));
        _stopwatches.Items.Add(new StopwatchDefinition("b", "Design", "#1F9D55", null, null, 1));
        _stopwatches.Items.Add(new StopwatchDefinition("free", "Walking", "#C96A0C", null, null, 2));
        _companies.Companies.Add(Company("c1", "ACME", "#2F6FEB", 0));
        _companies.Companies.Add(Company("c2", "Beta", "#1F9D55", 1));
        _companies.Assigned["c1"] = ["a"];
        _companies.Assigned["c2"] = ["b"];

        _sessions.Items.Add(new Session(1, "a", Utc(2026, 11, 2, 10), Utc(2026, 11, 2, 11)));
        _sessions.Items.Add(new Session(2, "a", Utc(2026, 11, 3, 9), Utc(2026, 11, 3, 9, 30)));

        var time = new FakeTimeProvider(Utc(2026, 11, 15, 12));
        var selector = new PdfConverterSelector([new StubConverter()]);
        _service = new InvoicingService(
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
        _selector = selector;
        _time = time;
    }

    private readonly PdfConverterSelector _selector;
    private readonly FakeTimeProvider _time;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static DateTimeOffset Utc(int y, int m, int d, int h, int min = 0) => new(y, m, d, h, min, 0, TimeSpan.Zero);

    private static Company Company(string id, string name, string color, int order) =>
        new(id, name, color, order, 60m, "EUR", null, null, null, "Invoice", true, null, 5);

    private InvoicingViewModel CreateViewModel(StubRates? rates = null) => new(
        _companies,
        _stopwatches,
        _service,
        _settings,
        _store,
        new DocxTemplateScanner(),
        _selector,
        _picker,
        _clipboard,
        _launcher,
        _dialog,
        new ImmediateDispatcher(),
        _time,
        new InvoiceDetailsViewModelFactory(_service, _companies, _stopwatches, _settings, _dialog, new ImmediateDispatcher(), _time),
        _request,
        rates ?? new StubRates());

    private void UseTemplate(string companyId)
    {
        var source = Path.Combine(_folder, "template.docx");
        Create(source, [Para(TextRun("Invoice {{period.month}}"))]);
        var stored = _store.Import(companyId, source);
        _companies.Update(_companies.Get(companyId)! with { TemplateFile = stored.FileName, TemplateName = stored.OriginalName, TemplateSha256 = stored.Sha256 });
    }

    [Fact]
    public async Task Copying_a_built_in_name_puts_its_marker_on_the_clipboard()
    {
        var vm = CreateViewModel();
        await WaitUntilAsync(() => !vm.Placeholders!.IsBusy);

        await vm.Placeholders!.CopyMarkerCommand.ExecuteAsync("period.month");

        Assert.Equal(["{{period.month}}"], _clipboard.Texts);
    }

    [Fact]
    public async Task Picking_a_template_adds_only_new_valid_markers_as_empty_static_rows()
    {
        _companies.Placeholders["c1"] = [new PlaceholderDefinition(0, "c1", "Client", PlaceholderKind.Static, "x", false, 0)];
        var source = Path.Combine(_folder, "picked.docx");
        Create(source, [Para(TextRun("{{period.month}} {{client}} {{poNumber}} {{bad alias}} {{poNumber}} {{ref}}"))]);
        _picker.OpenPath = source;
        var vm = CreateViewModel();

        await vm.CompanySettings!.PickTemplateCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => !vm.Placeholders!.IsBusy);

        var saved = _companies.GetPlaceholders("c1");
        Assert.Equal(["Client", "period.month", "poNumber", "ref"], saved.Select(p => p.Alias));
        Assert.Equal(PlaceholderKind.Automatic, saved[1].Kind);
        Assert.All(saved.Skip(2), p => Assert.Equal(PlaceholderKind.Static, p.Kind));
        Assert.All(saved.Skip(1), p => Assert.Equal(string.Empty, p.Value));
        Assert.Equal(4, vm.Placeholders!.Rows.Count);
    }

    [Fact]
    public async Task File_name_markers_get_rows_even_without_a_template()
    {
        _companies.Update(_companies.Get("c1")! with { FileNamePattern = "Invoice_{{period.month}}_{{poNumber}}_{{line.date}}" });

        var vm = CreateViewModel();
        await WaitUntilAsync(() => !vm.Placeholders!.IsBusy);

        var saved = _companies.GetPlaceholders("c1");
        Assert.Equal(["period.month", "poNumber"], saved.Select(p => p.Alias));
        Assert.Equal(PlaceholderKind.Automatic, saved[0].Kind);
        Assert.Equal(PlaceholderKind.Static, saved[1].Kind);
        Assert.Equal(["period.month", "poNumber"], vm.Placeholders!.Rows.Select(r => r.Alias));
    }

    [Fact]
    public async Task Changing_the_file_name_pattern_adds_rows_for_its_new_markers()
    {
        var vm = CreateViewModel();
        await WaitUntilAsync(() => !vm.Placeholders!.IsBusy);
        Assert.Empty(vm.Placeholders!.Rows);

        vm.CompanySettings!.FileNamePattern = "{{ref}}";
        vm.CompanySettings.SaveNow();
        await WaitUntilAsync(() => vm.Placeholders.Rows.Count == 1 && !vm.Placeholders.IsBusy);

        Assert.Equal(["ref"], _companies.GetPlaceholders("c1").Select(p => p.Alias));
    }

    [Fact]
    public async Task A_deleted_row_whose_marker_is_still_in_the_template_comes_back()
    {
        var source = Path.Combine(_folder, "picked.docx");
        Create(source, [Para(TextRun("{{poNumber}}"))]);
        _picker.OpenPath = source;
        var vm = CreateViewModel();
        await vm.CompanySettings!.PickTemplateCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => !vm.Placeholders!.IsBusy);

        vm.Placeholders!.Rows[0].DeleteCommand.Execute(null);
        vm.Placeholders.SaveNow();
        await WaitUntilAsync(() => !vm.Placeholders.IsBusy);

        Assert.Equal(["poNumber"], _companies.GetPlaceholders("c1").Select(p => p.Alias));
        Assert.Equal(["poNumber"], vm.Placeholders.Rows.Select(r => r.Alias));
        Assert.False(vm.Placeholders.HasWarnings);
    }

    [Fact]
    public async Task Loading_a_company_adds_rows_for_undefined_markers_of_an_earlier_template()
    {
        var source = Path.Combine(_folder, "earlier.docx");
        Create(source, [Para(TextRun("{{period.month}} {{poNumber}} {{bad alias}} {{Client}}"))]);
        var stored = _store.Import("c1", source);
        _companies.Update(_companies.Get("c1")! with { TemplateFile = stored.FileName, TemplateName = stored.OriginalName, TemplateSha256 = stored.Sha256 });
        _companies.Placeholders["c1"] = [new PlaceholderDefinition(0, "c1", "client", PlaceholderKind.Static, "x", false, 0)];

        var vm = CreateViewModel();
        await WaitUntilAsync(() => !vm.Placeholders!.IsBusy);

        Assert.Equal(["client", "period.month", "poNumber"], _companies.GetPlaceholders("c1").Select(p => p.Alias));
        Assert.Equal(["client", "period.month", "poNumber"], vm.Placeholders!.Rows.Select(r => r.Alias));
        Assert.Equal(string.Empty, vm.Placeholders.Rows[2].Value);
    }

    [Fact]
    public async Task Built_in_markers_get_automatic_rows_with_the_computed_value_and_line_markers_and_unused_built_ins_get_none()
    {
        var source = Path.Combine(_folder, "builtins.docx");
        Create(source, [Para(TextRun("{{hours}} {{line.date}} {{line.note}}"))]);
        var stored = _store.Import("c1", source);
        _companies.Update(_companies.Get("c1")! with { TemplateFile = stored.FileName, TemplateName = stored.OriginalName, TemplateSha256 = stored.Sha256 });

        var vm = CreateViewModel();
        await WaitUntilAsync(() => !vm.Placeholders!.IsBusy);

        var row = Assert.Single(vm.Placeholders!.Rows);
        Assert.Equal("hours", row.Alias);
        Assert.True(row.IsAutomatic);
        Assert.False(row.IsAliasEditable);
        Assert.True(row.CanBeAutomatic);
        Assert.False(row.ResultIsError);
        Assert.NotEqual(string.Empty, row.ResultText);
        Assert.False(vm.Placeholders.HasWarnings);
        Assert.Equal(PlaceholderKind.Automatic, Assert.Single(_companies.GetPlaceholders("c1")).Kind);
    }

    [Fact]
    public async Task Built_in_alias_is_normalized_and_duplicates_are_rejected_ignoring_case()
    {
        var vm = CreateViewModel();
        var placeholders = vm.Placeholders!;
        placeholders.AddCommand.Execute(null);
        placeholders.AddCommand.Execute(null);
        placeholders.Rows[0].Alias = "Hours";
        placeholders.Rows[1].Alias = "hours";

        Assert.False(placeholders.SaveNow());

        placeholders.Rows[1].Alias = "client";
        Assert.True(placeholders.SaveNow());
        await WaitUntilAsync(() => !placeholders.IsBusy);
        Assert.Equal(["hours", "client"], _companies.GetPlaceholders("c1").Select(p => p.Alias));
    }

    [Fact]
    public void First_company_is_selected_and_tabs_follow_selection()
    {
        var vm = CreateViewModel();

        Assert.Equal(["ACME", "Beta"], vm.Tabs.Select(t => t.Name));
        Assert.Equal("c1", vm.SelectedCompanyId);
        Assert.True(vm.Tabs[0].IsSelected);
        Assert.False(vm.Tabs[1].IsSelected);

        vm.Tabs[1].SelectCommand.Execute(null);

        Assert.Equal("c2", vm.SelectedCompanyId);
        Assert.False(vm.Tabs[0].IsSelected);
        Assert.True(vm.Tabs[1].IsSelected);
        Assert.Equal("Beta", vm.CompanySettings!.Name);
    }

    [Fact]
    public void Month_income_is_empty_without_allocated_time()
    {
        var vm = CreateViewModel();

        Assert.True(vm.MonthIncome.HasNoIncome);
        Assert.False(vm.MonthIncome.HasTotal);
    }

    [Fact]
    public async Task Month_income_shows_the_breakdown_and_the_total_in_usd()
    {
        AllocateAll();
        var vm = CreateViewModel();
        await WaitUntilAsync(() => vm.MonthIncome.HasTotal);

        Assert.True(vm.AmountsHidden);
        Assert.Contains(MoneyMask.Mask, vm.MonthIncome.TotalText);
        Assert.Contains(MoneyMask.Mask, vm.MonthIncome.Items[0].Text);

        vm.ToggleAmountsCommand.Execute(null);

        Assert.Single(vm.MonthIncome.Items);
        Assert.Contains("ACME", vm.MonthIncome.Items[0].Text);
        Assert.Contains("90", vm.MonthIncome.Items[0].Text);
        Assert.True(vm.MonthIncome.HasTotal);
        Assert.Contains("180", vm.MonthIncome.TotalText);
        Assert.Contains("USD", vm.MonthIncome.TotalText);
        Assert.True(vm.MonthIncome.HasHint);
        Assert.False(vm.MonthIncome.HasNoRate);
    }

    [Fact]
    public async Task Month_income_without_rates_has_no_total()
    {
        AllocateAll();
        var vm = CreateViewModel(new StubRates { Rates = null });
        await WaitUntilAsync(() => vm.MonthIncome.HasHint);

        Assert.Single(vm.MonthIncome.Items);
        Assert.False(vm.MonthIncome.HasTotal);
        Assert.True(vm.MonthIncome.HasHint);
    }

    [Fact]
    public async Task Month_income_leaves_a_currency_without_a_rate_out_of_the_total()
    {
        AllocateAll();
        var vm = CreateViewModel(new StubRates { Rates = new([new("CAD", 1.5m)], new DateTimeOffset(2026, 11, 15, 11, 0, 0, TimeSpan.Zero)) });
        await WaitUntilAsync(() => vm.MonthIncome.HasNoRate);

        Assert.False(vm.MonthIncome.HasTotal);
        Assert.True(vm.MonthIncome.HasNoRate);
        Assert.Contains("EUR", vm.MonthIncome.NoRateText);
    }

    [Fact]
    public void Amounts_are_masked_until_shown()
    {
        AllocateAll();
        var vm = CreateViewModel();

        Assert.True(vm.AmountsHidden);
        Assert.Equal(MoneyMask.Mask, vm.CompanySettings!.RateDisplay);
        Assert.True(vm.CompanySettings.IsRateReadOnly);
        Assert.Equal(MoneyMask.Mask, vm.Invoices!.Rows[0].AmountText);

        vm.CompanySettings.RateDisplay = "99"; // ignored while hidden
        Assert.Equal("60", vm.CompanySettings.RateText);

        vm.ToggleAmountsCommand.Execute(null);

        Assert.False(vm.AmountsHidden);
        Assert.Equal("60", vm.CompanySettings.RateDisplay);
        Assert.False(vm.CompanySettings.IsRateReadOnly);
        Assert.Equal("90.00 EUR", vm.Invoices.Rows[0].AmountText.Replace(' ', ' ').Replace(' ', ' '));
        Assert.False(vm.Preview!.IsCoverVisible);
    }

    [Fact]
    public void Stopwatch_owned_by_another_company_is_disabled_with_its_name()
    {
        var vm = CreateViewModel();

        var chips = vm.CompanySettings!.Chips.ToDictionary(c => c.StopwatchId);

        Assert.True(chips["a"].IsAssigned);
        Assert.True(chips["a"].IsFree);
        Assert.True(chips["b"].IsTaken);
        Assert.Equal("Beta", chips["b"].OwnerName);
        Assert.False(chips["b"].IsAssigned);
        Assert.True(chips["free"].IsFree);
    }

    [Fact]
    public void Toggling_a_chip_changes_the_assignment()
    {
        var vm = CreateViewModel();

        vm.CompanySettings!.Chips.First(c => c.StopwatchId == "free").IsAssigned = true;

        Assert.Equal(new HashSet<string> { "a", "free" }, _companies.Assigned["c1"]);
    }

    [Fact]
    public void Hidden_stopwatch_shows_only_when_assigned()
    {
        _stopwatches.Items.Add(new StopwatchDefinition("gone", "Old", "#2F6FEB", null, null, 3, IsDeleted: true));
        _stopwatches.Items.Add(new StopwatchDefinition("gone2", "Older", "#2F6FEB", null, null, 4, IsDeleted: true));
        _companies.Assigned["c1"].Add("gone");

        var vm = CreateViewModel();

        Assert.Contains(vm.CompanySettings!.Chips, c => c.StopwatchId == "gone");
        Assert.DoesNotContain(vm.CompanySettings.Chips, c => c.StopwatchId == "gone2");
    }

    [Fact]
    public void Add_company_uses_defaults_and_the_next_free_color()
    {
        var vm = CreateViewModel();

        vm.AddCompanyCommand.Execute(null);

        var added = _companies.Companies.Last();
        Assert.Equal("Invoice_{{period.month}}", added.FileNamePattern);
        Assert.DoesNotContain(added.Color, new[] { "#2F6FEB", "#1F9D55" });
        Assert.Equal(added.Id, vm.SelectedCompanyId);
        Assert.Equal(3, vm.Tabs.Count);
    }

    [Fact]
    public void Deleting_a_company_asks_first_and_selects_another()
    {
        var vm = CreateViewModel();

        vm.CompanySettings!.DeleteCommand.Execute(null);
        Assert.True(_dialog.IsOpen);
        Assert.False(_companies.Get("c1")!.IsDeleted);

        _dialog.Confirm();

        Assert.True(_companies.Get("c1")!.IsDeleted);
        Assert.Equal("c2", vm.SelectedCompanyId);
        Assert.Single(vm.Tabs);
    }

    [Fact]
    public void The_day_start_is_validated_and_saved()
    {
        var vm = CreateViewModel();
        var settings = vm.CompanySettings!;
        Assert.Equal("00:00", settings.DayStartInput);

        settings.DayStartInput = "24:30";
        settings.SaveNow();
        Assert.True(settings.HasDayStartError);
        Assert.Equal(0, _companies.Get("c1")!.DayStartMinutes);

        settings.DayStartInput = "20:00";
        settings.SaveNow();
        Assert.False(settings.HasDayStartError);
        Assert.Equal(20 * 60, _companies.Get("c1")!.DayStartMinutes);
    }

    [Fact]
    public void The_next_invoice_number_is_validated_and_saved()
    {
        var vm = CreateViewModel();
        var settings = vm.CompanySettings!;
        Assert.Equal("5", settings.NextNumberText);

        settings.NextNumberText = "0";
        settings.SaveNow();
        Assert.True(settings.HasNextNumberError);
        Assert.Equal(5, _companies.Get("c1")!.NextInvoiceNumber);

        settings.NextNumberText = "12";
        settings.SaveNow();
        Assert.False(settings.HasNextNumberError);
        Assert.Equal(12, _companies.Get("c1")!.NextInvoiceNumber);
    }

    [Fact]
    public void Saving_other_fields_does_not_overwrite_a_counter_advanced_by_sending()
    {
        var vm = CreateViewModel();
        var settings = vm.CompanySettings!;
        var company = _companies.Get("c1")!;
        _companies.Update(company with { NextInvoiceNumber = 9 });

        settings.Name = "ACME Corp";
        settings.SaveNow();

        Assert.Equal(9, _companies.Get("c1")!.NextInvoiceNumber);
    }

    [Fact]
    public void Edits_are_saved_by_flush()
    {
        var vm = CreateViewModel();
        vm.ToggleAmountsCommand.Execute(null);

        vm.CompanySettings!.Name = "ACME Corp";
        vm.CompanySettings.RateText = "75,5";
        vm.CompanySettings.Currency = "usd";
        vm.CompanySettings.SaveNow();

        var saved = _companies.Get("c1")!;
        Assert.Equal("ACME Corp", saved.Name);
        Assert.Equal(75.5m, saved.Rate);
        Assert.Equal("USD", saved.Currency);
        Assert.Equal("ACME Corp", vm.Tabs[0].Name);
    }

    [Fact]
    public void Invalid_alias_blocks_saving_and_shows_the_error()
    {
        var vm = CreateViewModel();
        var placeholders = vm.Placeholders!;

        placeholders.AddCommand.Execute(null);
        placeholders.Rows[0].Alias = "line.date"; // reserved

        Assert.False(placeholders.SaveNow());
        Assert.True(placeholders.Rows[0].HasAliasError);
        Assert.Empty(_companies.GetPlaceholders("c1"));

        placeholders.Rows[0].Alias = "client";
        placeholders.Rows[0].Value = "Kestrel";
        Assert.True(placeholders.SaveNow());
        Assert.Single(_companies.GetPlaceholders("c1"));
    }

    [Fact]
    public async Task Placeholder_result_is_masked_only_when_hidden_with_amounts()
    {
        _companies.Placeholders["c1"] = [new PlaceholderDefinition(1, "c1", "client", PlaceholderKind.Static, "Kestrel", true, 0)];
        var vm = CreateViewModel();
        var row = vm.Placeholders!.Rows[0];
        await WaitUntilAsync(() => !vm.Placeholders.IsBusy);

        Assert.Equal(MoneyMask.Mask, row.ResultText);

        vm.ToggleAmountsCommand.Execute(null);
        Assert.Equal("Kestrel", row.ResultText);

        row.Hidden = false;
        vm.ToggleAmountsCommand.Execute(null);
        Assert.Equal("Kestrel", row.ResultText);
    }

    [Fact]
    public async Task Generate_warns_about_unallocated_time_first()
    {
        var vm = CreateViewModel();
        var row = vm.Invoices!.Rows[0];
        Assert.True(row.HasUnallocated);
        _picker.SavePath = null;

        await row.GenerateCommand.ExecuteAsync(null);

        Assert.True(_dialog.IsOpen);
        Assert.Equal(0, _picker.SaveCalls);

        _dialog.Confirm();
        await Task.Yield();

        Assert.Equal(1, _picker.SaveCalls); // cancelled in the picker: nothing generated
        Assert.Null(_invoices.Get("c1", November));
    }

    [Fact]
    public async Task Generate_without_a_folder_reports_it()
    {
        _companies.Update(_companies.Get("c1")! with { AskDestination = false, OutputFolder = null });
        AllocateAll();
        var vm = CreateViewModel();

        await vm.Invoices!.Rows[0].GenerateCommand.ExecuteAsync(null);

        Assert.True(vm.Invoices.IsStatusError);
        Assert.False(_dialog.IsOpen);
        Assert.Null(_invoices.Get("c1", November)!.FilePath);
    }

    [Fact]
    public async Task Generate_into_a_folder_asks_before_replacing_a_file()
    {
        UseTemplate("c1");
        var output = Path.Combine(_folder, "out");
        Directory.CreateDirectory(output);
        _companies.Update(_companies.Get("c1")! with { AskDestination = false, OutputFolder = output });
        AllocateAll();
        var vm = CreateViewModel();
        var expected = Path.Combine(output, _service.SuggestFileName("c1", November));
        File.WriteAllText(expected, "old");

        await vm.Invoices!.Rows[0].GenerateCommand.ExecuteAsync(null);

        Assert.True(_dialog.IsOpen);
        Assert.Equal("old", File.ReadAllText(expected));

        _dialog.Confirm();
        await WaitUntilAsync(() => !vm.Invoices.IsBusy && vm.Invoices.HasStatus);

        Assert.False(vm.Invoices.IsStatusError);
        Assert.NotEqual("old", File.ReadAllText(expected));
        Assert.True(vm.Invoices.Rows[0].IsPending);
    }

    [Fact]
    public async Task Marking_sent_after_a_change_asks_for_confirmation()
    {
        UseTemplate("c1");
        AllocateAll();
        var path = Path.Combine(_folder, "invoice.pdf");
        File.WriteAllText(path, "pdf");
        var invoice = _invoices.GetOrCreate("c1", November);
        _invoices.MarkGenerated(invoice.Id, path, _time.GetUtcNow(), "stale-hash");
        var vm = CreateViewModel();
        var row = vm.Invoices!.Rows[0];
        Assert.True(row.IsPending);

        row.ToggleSentCommand.Execute(null);

        Assert.True(_dialog.IsOpen);
        Assert.Equal(InvoiceStatus.Pending, _invoices.Get("c1", November)!.Status);

        _dialog.Confirm();

        Assert.Equal(InvoiceStatus.Sent, _invoices.Get("c1", November)!.Status);
        Assert.True(vm.Invoices.Rows[0].IsSent);
        Assert.False(vm.Invoices.Rows[0].GenerateCommand.CanExecute(null));
        await WaitUntilAsync(() => !vm.Placeholders!.IsBusy);
    }

    [Fact]
    public void A_navigation_request_opens_the_details_once()
    {
        _request.Set("c2", November);
        var vm = CreateViewModel();
        Assert.Null(vm.CurrentDetails);

        vm.OnNavigatedTo();

        Assert.Equal("c2", vm.SelectedCompanyId);
        Assert.IsType<InvoiceDetailsViewModel>(vm.CurrentDetails);
        Assert.Null(_request.Take());

        vm.CloseDetails();
        vm.OnNavigatedTo();

        Assert.Null(vm.CurrentDetails);
    }

    [Fact]
    public void A_navigation_request_for_a_missing_company_opens_nothing()
    {
        _request.Set("gone", November);
        var vm = CreateViewModel();

        vm.OnNavigatedTo();

        Assert.Equal("c1", vm.SelectedCompanyId);
        Assert.Null(vm.CurrentDetails);
    }

    [Fact]
    public void Show_details_switches_the_content_and_back()
    {
        var vm = CreateViewModel();

        vm.Invoices!.Rows[0].ShowDetailsCommand.Execute(null);

        Assert.IsType<InvoiceDetailsViewModel>(vm.CurrentDetails);
        Assert.False(vm.IsPageVisible);

        ((InvoiceDetailsViewModel)vm.CurrentDetails!).BackCommand.Execute(null);

        Assert.Null(vm.CurrentDetails);
        Assert.True(vm.IsPageVisible);
    }

    [Fact]
    public void Open_file_is_disabled_without_a_file()
    {
        var vm = CreateViewModel();

        Assert.False(vm.Invoices!.Rows[0].OpenFileCommand.CanExecute(null));
        Assert.False(vm.Invoices.Rows[0].ToggleSentCommand.CanExecute(null));
    }

    private void AllocateAll()
    {
        _service.EditDay("c1", November, new DateOnly(2026, 11, 2), lines => InvoiceLineEditor.SetNote(lines, 0, "work"));
        _service.EditDay("c1", November, new DateOnly(2026, 11, 3), lines => InvoiceLineEditor.SetNote(lines, 0, "work"));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25);
        }

        Assert.True(condition());
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

    private sealed class FakeClipboard : IClipboardService
    {
        public List<string> Texts { get; } = [];

        public Task<bool> SetTextAsync(string text)
        {
            Texts.Add(text);
            return Task.FromResult(true);
        }
    }

    private sealed class FakePicker : IFilePickerService
    {
        public string? SavePath { get; set; }

        public int SaveCalls { get; private set; }

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public string? OpenPath { get; set; }

        public Task<string?> PickOpenFileAsync(string title, string filterName, IReadOnlyList<string> extensions) => Task.FromResult(OpenPath);

        public Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension)
        {
            SaveCalls++;
            return Task.FromResult(SavePath);
        }
    }

    private sealed class StubRates : IExchangeRateService
    {
        public ExchangeRates? Rates { get; set; } = new([new("EUR", 0.5m)], new DateTimeOffset(2026, 11, 15, 11, 0, 0, TimeSpan.Zero));

        public Task<ExchangeRates?> GetRatesAsync(CancellationToken cancellationToken) => Task.FromResult(Rates);
    }

    private sealed class FakeLauncher : IInvoiceFileLauncher
    {
        public bool OpenFile(string path) => true;

        public bool ShowInFolder(string path) => true;
    }

    private sealed class StubConverter : IPdfConverter
    {
        public string Name => "Stub";

        public bool IsAvailable => true;

        public Task ConvertAsync(string docxPath, string outputPdfPath, CancellationToken cancellationToken)
        {
            File.WriteAllText(outputPdfPath, "%PDF-stub");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Reported_hours_are_set_from_the_row_editor_and_show_the_difference()
    {
        AllocateAll();
        var vm = CreateViewModel();
        var row = vm.Invoices!.Rows[0];

        row.SetReportedCommand.Execute(null);
        Assert.True(row.IsEditingReported);
        Assert.Equal("001:30", row.ReportedInput);

        row.ReportedInput = "2";
        row.SaveReportedCommand.Execute(null);

        var saved = vm.Invoices.Rows[0];
        Assert.False(saved.IsEditingReported);
        Assert.Equal(120, _service.GetSummary("c1", November).ReportedMinutes);
        Assert.Equal("2h", saved.HoursText);
        Assert.True(saved.HasReported);
        Assert.Equal("+30m vs tracked", saved.ReportedDifferenceText);

        saved.SetReportedCommand.Execute(null);
        Assert.Equal("002:00", saved.ReportedInput);
        saved.ClearReportedCommand.Execute(null);

        Assert.Null(_service.GetSummary("c1", November).ReportedMinutes);
        Assert.False(vm.Invoices.Rows[0].HasReported);
    }

    [Fact]
    public void The_row_editor_rejects_invalid_text_cancels_and_is_disabled_when_sent()
    {
        AllocateAll();
        var vm = CreateViewModel();
        var row = vm.Invoices!.Rows[0];

        row.SetReportedCommand.Execute(null);
        row.ReportedInput = "x";
        Assert.True(row.ReportedInvalid);
        Assert.False(row.CanSaveReported);
        row.SaveReportedCommand.Execute(null);
        Assert.Null(_service.GetSummary("c1", November).ReportedMinutes);

        row.CancelReportedCommand.Execute(null);
        Assert.False(row.IsEditingReported);

        _invoices.GetOrCreate("c1", November);
        Assert.Equal(MarkSentResult.Sent, _service.MarkSent("c1", November, confirmChanged: false));
        vm.Invoices.Refresh();
        Assert.False(vm.Invoices.Rows[0].SetReportedCommand.CanExecute(null));
    }
}
