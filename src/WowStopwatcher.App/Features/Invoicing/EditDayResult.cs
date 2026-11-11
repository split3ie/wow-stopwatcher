namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Outcome of changing a day of an invoice.</summary>
public enum EditDayResult
{
    /// <summary>The change was stored.</summary>
    Saved,

    /// <summary>Nothing was stored because the invoice is sent (read-only).</summary>
    InvoiceSent,

    /// <summary>Nothing was stored because there was nothing to change or the result would be invalid.</summary>
    Unchanged,
}
