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
        new(id, name, color, order, 60m, "EUR", null, null, null, "Invoice {{period.month}}", true, null, 5);

    private InvoicingViewModel CreateViewModel() => new(
        _companies,
        _stopwatches,
        _service,
        _settings,
        _store,
        new DocxTemplateScanner(),
        _selector,
        _picker,
        _launcher,
        _dialog,
        new ImmediateDispatcher(),
        _time,
        new InvoiceDetailsViewModelFactory(_service, _companies, _stopwatches, _settings, _dialog, new ImmediateDispatcher(), _time),
        _request);

    private void UseTemplate(string companyId)
    {
        var source = Path.Combine(_folder, "template.docx");
        Create(source, [Para(TextRun("Invoice {{period.month}}"))]);
        var stored = _store.Import(companyId, source);
        _companies.Update(_companies.Get(companyId)! with { TemplateFile = stored.FileName, TemplateName = stored.OriginalName, TemplateSha256 = stored.Sha256 });
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
        placeholders.Rows[0].Alias = "hours"; // built in

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

    private sealed class FakePicker : IFilePickerService
    {
        public string? SavePath { get; set; }

        public int SaveCalls { get; private set; }

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickOpenFileAsync(string title, string filterName, IReadOnlyList<string> extensions) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension)
        {
            SaveCalls++;
            return Task.FromResult(SavePath);
        }
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
}
