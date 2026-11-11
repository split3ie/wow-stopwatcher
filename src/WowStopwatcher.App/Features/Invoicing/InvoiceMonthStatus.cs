namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Status of a month in the invoices list.</summary>
public enum InvoiceMonthStatus
{
    /// <summary>No invoice row or no generated file yet.</summary>
    Draft,

    /// <summary>A file was generated; the invoice can still change.</summary>
    Pending,

    /// <summary>Marked as sent; frozen.</summary>
    Sent,
}
