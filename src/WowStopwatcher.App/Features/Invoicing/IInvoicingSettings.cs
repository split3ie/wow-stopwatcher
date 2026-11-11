namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Invoicing settings that are remembered across restarts.</summary>
public interface IInvoicingSettings
{
    /// <summary>True when rate and money are shown as masked text on screen. On by default. Display only.</summary>
    bool HideAmounts { get; set; }

    /// <summary>Raised after <see cref="HideAmounts"/> changed.</summary>
    event EventHandler? HideAmountsChanged;
}
