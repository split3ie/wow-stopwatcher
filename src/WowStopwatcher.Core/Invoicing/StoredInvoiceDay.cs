namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// A day whose lines are stored with the invoice.
/// </summary>
/// <param name="Date">Local day.</param>
/// <param name="Edited">True when the user edited the day; false when it is stored only because the invoice is sent.</param>
/// <param name="Lines">The stored lines in order.</param>
public sealed record StoredInvoiceDay(DateOnly Date, bool Edited, IReadOnlyList<InvoiceLine> Lines);
