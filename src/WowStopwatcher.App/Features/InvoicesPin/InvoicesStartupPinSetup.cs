using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Features.InvoicesPin;

/// <summary>Stores the PIN chosen in the "Protect your data" window (or turns the PIN off).</summary>
public sealed class InvoicesStartupPinSetup(InvoicesPinService service) : IStartupPinSetup
{
    public void Apply(string? pin)
    {
        if (pin is null)
        {
            service.Disable();
        }
        else
        {
            service.Set(pin);
        }
    }
}
