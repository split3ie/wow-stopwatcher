using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>
/// A one-time request to open an invoice's details on the Invoicing page, made from outside it (for example the Statistics dialog).
/// The requester sets it and navigates to the page; the page takes it when it is shown.
/// </summary>
public sealed class InvoicingNavigationRequest
{
    private (string CompanyId, InvoicePeriod Period)? _pending;

    /// <summary>Asks the Invoicing page to open the invoice of <paramref name="companyId"/> for <paramref name="period"/>.</summary>
    public void Set(string companyId, InvoicePeriod period) => _pending = (companyId, period);

    /// <summary>Returns the pending request and clears it; null when there is none.</summary>
    public (string CompanyId, InvoicePeriod Period)? Take()
    {
        var request = _pending;
        _pending = null;
        return request;
    }
}
