namespace WowStopwatcher.Core.Invoicing;

/// <summary>Lifecycle of an invoice.</summary>
public enum InvoiceStatus
{
    /// <summary>Open: untouched days follow the tracked time.</summary>
    Pending,

    /// <summary>Sent: every day is stored and the invoice no longer changes.</summary>
    Sent,
}
