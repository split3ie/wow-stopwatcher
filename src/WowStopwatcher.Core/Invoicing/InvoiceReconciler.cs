namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// Combines live days (from sessions) with the days stored in an invoice.
/// </summary>
public static class InvoiceReconciler
{
    /// <summary>
    /// Returns the union of dates ordered by date. A stored day wins for its lines and <see cref="InvoiceDay.Edited"/>;
    /// tracked minutes and the running flag always come from the live day (0 and false when there is none).
    /// </summary>
    public static IReadOnlyList<InvoiceDay> Compose(IReadOnlyList<InvoiceDay> liveDays, IReadOnlyList<StoredInvoiceDay> storedDays)
    {
        var live = liveDays.ToDictionary(d => d.Date);
        var stored = storedDays.ToDictionary(d => d.Date);

        return live.Keys.Union(stored.Keys)
            .Order()
            .Select(date =>
            {
                live.TryGetValue(date, out var l);
                var tracked = l?.TrackedMinutes ?? 0;
                var running = l?.HasRunningSession ?? false;
                return stored.TryGetValue(date, out var s)
                    ? new InvoiceDay(date, s.Edited, s.Lines, tracked, running)
                    : new InvoiceDay(date, false, l!.Lines, tracked, running);
            })
            .ToList();
    }
}
