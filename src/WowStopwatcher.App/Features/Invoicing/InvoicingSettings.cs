using WowStopwatcher.Core;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary><see cref="IInvoicingSettings"/> over the settings repository: stored as "true"/"false", a missing value means hidden.</summary>
public sealed class InvoicingSettings : IInvoicingSettings
{
    public const string HideAmountsKey = "invoicing_hide_amounts";

    private readonly ISettingsRepository _settings;

    public InvoicingSettings(ISettingsRepository settings) => _settings = settings;

    public event EventHandler? HideAmountsChanged;

    public bool HideAmounts
    {
        get => _settings.Get(HideAmountsKey) != "false";
        set
        {
            if (value == HideAmounts)
            {
                return;
            }

            _settings.Set(HideAmountsKey, value ? "true" : "false");
            HideAmountsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
