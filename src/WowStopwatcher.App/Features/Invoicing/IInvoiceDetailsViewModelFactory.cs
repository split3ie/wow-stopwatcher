using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Creates the view model of the details view that replaces the Invoicing page content for one invoice.</summary>
public interface IInvoiceDetailsViewModelFactory
{
    /// <summary>Creates the view model; its view is found through a registered view mapping. <paramref name="close"/> returns to the page.</summary>
    object Create(string companyId, InvoicePeriod period, Action close);
}
