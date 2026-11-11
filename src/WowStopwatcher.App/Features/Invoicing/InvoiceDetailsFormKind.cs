namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Which inline form of the invoice details toolbar is open.</summary>
public enum InvoiceDetailsFormKind
{
    /// <summary>No form.</summary>
    None,

    /// <summary>Split the selected line into two.</summary>
    Split,

    /// <summary>Set a new time for the selected line.</summary>
    Adjust,
}
