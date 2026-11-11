using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>One day of an invoice as one stopwatch sees it.</summary>
/// <param name="Lines">All lines of the day in the invoice (every stopwatch).</param>
/// <param name="TrackedMinutes">Minutes the stopwatch ran that day, rounded to whole minutes.</param>
/// <param name="IsSent">True when the invoice is sent, so the day cannot change.</param>
public sealed record InvoiceTimeDay(IReadOnlyList<InvoiceLine> Lines, int TrackedMinutes, bool IsSent);
