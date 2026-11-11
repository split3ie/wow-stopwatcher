using WowStopwatcher.App.Navigation;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>The "Invoice" button of Statistics: opens the time and notes of a billed stopwatch's day.</summary>
public sealed class InvoiceTimeStatisticsAction : IStatisticsStopwatchAction
{
    private readonly ICompanyRepository _companies;
    private readonly IStopwatchRepository _stopwatches;
    private readonly InvoicingService _service;
    private readonly IMainWindowService _mainWindow;
    private readonly INavigationService _navigation;
    private readonly InvoicingNavigationRequest _request;

    public InvoiceTimeStatisticsAction(
        ICompanyRepository companies,
        IStopwatchRepository stopwatches,
        InvoicingService service,
        IMainWindowService mainWindow,
        INavigationService navigation,
        InvoicingNavigationRequest request)
    {
        _companies = companies;
        _stopwatches = stopwatches;
        _service = service;
        _mainWindow = mainWindow;
        _navigation = navigation;
        _request = request;
    }

    public string Label => Loc.T("invoiceTime.action");

    public bool AppliesTo(string stopwatchId) => _companies.GetStopwatchOwners().ContainsKey(stopwatchId);

    public void Invoke(string stopwatchId, DateOnly date)
    {
        if (!_companies.GetStopwatchOwners().TryGetValue(stopwatchId, out var companyId)
            || _companies.Get(companyId) is not { } company
            || _stopwatches.Get(stopwatchId) is not { } stopwatch)
        {
            return;
        }

        var dialog = new InvoiceTimeDialog();
        dialog.DataContext = new InvoiceTimeViewModel(
            _service,
            company.Id,
            company.Name,
            stopwatch,
            date,
            dialog.Close,
            () => OpenInvoice(dialog, company.Id, InvoicePeriod.From(date)));
        _ = dialog.ShowDialog(_mainWindow.Owner);
    }

    private void OpenInvoice(InvoiceTimeDialog dialog, string companyId, InvoicePeriod period)
    {
        _request.Set(companyId, period);
        dialog.Close();
        _navigation.NavigateTo(PageKeys.Invoicing);
    }
}
