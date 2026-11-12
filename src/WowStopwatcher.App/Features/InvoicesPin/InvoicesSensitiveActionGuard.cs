using WowStopwatcher.App.Shell;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.InvoicesPin;

/// <summary>Asks for the Invoices PIN before export, import or a database folder change. Allows everything when no PIN is active.</summary>
public sealed class InvoicesSensitiveActionGuard(InvoicesPinService service, InvoicesPinPrompts prompts) : ISensitiveActionGuard
{
    public Task<bool> ConfirmAsync() =>
        service.IsEnabled
            ? prompts.ConfirmCurrentAsync(Loc.T("invoicing.pin.guard.title"), Loc.T("invoicing.pin.guard.confirm"))
            : Task.FromResult(true);
}
