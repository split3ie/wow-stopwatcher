using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Creates <see cref="InvoiceDetailsViewModel"/> with the services it needs.</summary>
public sealed class InvoiceDetailsViewModelFactory : IInvoiceDetailsViewModelFactory
{
    private readonly InvoicingService _service;
    private readonly ICompanyRepository _companies;
    private readonly IStopwatchRepository _stopwatches;
    private readonly IInvoicingSettings _settings;
    private readonly IDialogService _dialog;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _time;

    public InvoiceDetailsViewModelFactory(
        InvoicingService service,
        ICompanyRepository companies,
        IStopwatchRepository stopwatches,
        IInvoicingSettings settings,
        IDialogService dialog,
        IUiDispatcher dispatcher,
        TimeProvider time)
    {
        _service = service;
        _companies = companies;
        _stopwatches = stopwatches;
        _settings = settings;
        _dialog = dialog;
        _dispatcher = dispatcher;
        _time = time;
    }

    public object Create(string companyId, InvoicePeriod period, Action close) =>
        new InvoiceDetailsViewModel(_service, _companies, _stopwatches, _settings, _dialog, _dispatcher, _time, companyId, period, close);
}
