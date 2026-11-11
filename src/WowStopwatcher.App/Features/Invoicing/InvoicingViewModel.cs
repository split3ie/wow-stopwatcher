using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Navigation;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>
/// The Invoicing page: company tabs and, for the selected company, the settings card, placeholders, preview and invoices list.
/// The content can be replaced by a details view (<see cref="CurrentDetails"/>).
/// </summary>
public sealed partial class InvoicingViewModel : ObservableObject, INavigationAware
{
    private readonly InvoicingDependencies _d;
    private readonly IInvoicingSettings _settings;
    private readonly IInvoiceDetailsViewModelFactory _detailsFactory;
    private readonly InvoicingNavigationRequest _request;

    public InvoicingViewModel(
        ICompanyRepository companies,
        IStopwatchRepository stopwatches,
        InvoicingService service,
        IInvoicingSettings settings,
        InvoiceTemplateStore templates,
        DocxTemplateScanner scanner,
        PdfConverterSelector converter,
        IFilePickerService picker,
        IClipboardService clipboard,
        IInvoiceFileLauncher files,
        IDialogService dialog,
        IUiDispatcher dispatcher,
        TimeProvider time,
        IInvoiceDetailsViewModelFactory detailsFactory,
        InvoicingNavigationRequest request)
    {
        _d = new InvoicingDependencies(companies, stopwatches, service, templates, scanner, converter, picker, clipboard, files, dialog, dispatcher, time);
        _settings = settings;
        _detailsFactory = detailsFactory;
        _request = request;
        _amountsHidden = settings.HideAmounts;
        settings.HideAmountsChanged += OnHideAmountsChanged;
        LoadTabs(null);
    }

    public ObservableCollection<CompanyTabViewModel> Tabs { get; } = [];

    public bool HasCompanies => Tabs.Count > 0;

    public bool HasNoCompanies => Tabs.Count == 0;

    public string? SelectedCompanyId { get; private set; }

    [ObservableProperty]
    private CompanySettingsViewModel? _companySettings;

    [ObservableProperty]
    private PlaceholdersViewModel? _placeholders;

    [ObservableProperty]
    private PreviewViewModel? _preview;

    [ObservableProperty]
    private InvoicesViewModel? _invoices;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HideButtonText))]
    private bool _amountsHidden;

    [ObservableProperty]
    private bool _isSettingsOpen = true;

    /// <summary>The details view model that replaces the page content; null shows the page.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailsOpen))]
    [NotifyPropertyChangedFor(nameof(IsPageVisible))]
    private object? _currentDetails;

    public bool IsDetailsOpen => CurrentDetails is not null;

    public bool IsPageVisible => CurrentDetails is null;

    public string HideButtonText => AmountsHidden ? Loc.T("invoicing.showAmounts") : Loc.T("invoicing.hideAmounts");

    public void OnNavigatedTo()
    {
        // A request made elsewhere (for example the Statistics dialog) opens that invoice's details once.
        if (_request.Take() is { } request)
        {
            SelectCompany(request.CompanyId);
            if (SelectedCompanyId == request.CompanyId)
            {
                ShowDetails(request.Period);
            }
        }
    }

    public void OnNavigatedFrom()
    {
        (CurrentDetails as IDisposable)?.Dispose();
        _settings.HideAmountsChanged -= OnHideAmountsChanged;
        FlushAndStop();
    }

    /// <summary>Opens the details view of the selected company's invoice for <paramref name="period"/>.</summary>
    public void ShowDetails(InvoicePeriod period)
    {
        if (SelectedCompanyId is { } id)
        {
            (CurrentDetails as IDisposable)?.Dispose();
            CurrentDetails = _detailsFactory.Create(id, period, CloseDetails);
        }
    }

    /// <summary>Returns from the details view to the page and reads the invoices again.</summary>
    public void CloseDetails()
    {
        (CurrentDetails as IDisposable)?.Dispose();
        CurrentDetails = null;
        Invoices?.Refresh();
    }

    [RelayCommand]
    private void ToggleAmounts() => _settings.HideAmounts = !_settings.HideAmounts;

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    /// <summary>Selects the tab of the company and builds its parts.</summary>
    public void SelectCompany(string companyId)
    {
        if (_d.Companies.Get(companyId) is not { IsDeleted: false } company)
        {
            return;
        }

        FlushAndStop();
        (CurrentDetails as IDisposable)?.Dispose();
        CurrentDetails = null;
        SelectedCompanyId = companyId;
        foreach (var tab in Tabs)
        {
            tab.IsSelected = tab.Id == companyId;
        }

        var preview = new PreviewViewModel(_d, companyId, AmountsHidden, () => _ = Placeholders?.RunAsync(), () => _settings.HideAmounts = false);
        Preview = preview;
        Placeholders = new PlaceholdersViewModel(_d, companyId, () => preview.SampleHours, AmountsHidden);
        Invoices = new InvoicesViewModel(_d, companyId, AmountsHidden, ShowDetails, () => CompanySettings?.RefreshNextNumber());
        CompanySettings = new CompanySettingsViewModel(_d, company, AmountsHidden, OnCompanySaved, OnCompanyDeleted);
    }

    [RelayCommand]
    private void AddCompany()
    {
        var companies = _d.Companies.GetAll(includeDeleted: false);
        var company = new Company(
            Company.NewId(),
            Loc.T("invoicing.company.defaultName"),
            InvoicingColors.NextFree(companies.Select(c => c.Color).ToList()),
            companies.Count == 0 ? 0 : companies.Max(c => c.SortOrder) + 1,
            0m,
            "USD",
            null,
            null,
            null,
            "Invoice_{{period.month}}",
            true,
            null,
            1);
        _d.Companies.Insert(company);
        LoadTabs(company.Id);
        IsSettingsOpen = true;
    }

    private void LoadTabs(string? select)
    {
        Tabs.Clear();
        var companies = _d.Companies.GetAll(includeDeleted: false);
        foreach (var company in companies)
        {
            Tabs.Add(new CompanyTabViewModel(company, false, SelectCompany));
        }

        OnPropertyChanged(nameof(HasCompanies));
        OnPropertyChanged(nameof(HasNoCompanies));

        var target = select ?? SelectedCompanyId;
        if (target is null || companies.All(c => c.Id != target))
        {
            target = companies.Count > 0 ? companies[0].Id : null;
        }

        if (target is null)
        {
            SelectedCompanyId = null;
            CompanySettings = null;
            Placeholders = null;
            Preview = null;
            Invoices = null;
            return;
        }

        SelectCompany(target);
    }

    private void OnCompanySaved()
    {
        if (_d.Companies.Get(SelectedCompanyId ?? string.Empty) is { } company)
        {
            Tabs.FirstOrDefault(t => t.Id == company.Id)?.Update(company);
        }

        Invoices?.Refresh();
        _ = Placeholders?.RunAsync();
        Preview?.ClearForCompanyChange();
    }

    private void OnCompanyDeleted()
    {
        CompanySettings = null;
        SelectedCompanyId = null;
        LoadTabs(null);
    }

    private void OnHideAmountsChanged(object? sender, EventArgs e)
    {
        AmountsHidden = _settings.HideAmounts;
        CompanySettings?.SetAmountsHidden(AmountsHidden);
        Placeholders?.SetAmountsHidden(AmountsHidden);
        Preview?.SetAmountsHidden(AmountsHidden);
        Invoices?.SetAmountsHidden(AmountsHidden);
    }

    private void FlushAndStop()
    {
        CompanySettings?.Flush();
        Placeholders?.Flush();
        Preview?.Cancel();
    }
}
