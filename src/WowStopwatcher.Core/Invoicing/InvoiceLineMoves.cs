namespace WowStopwatcher.Core.Invoicing;

/// <summary>Helpers for the moved-in portions of lines.</summary>
public static class InvoiceLineMoves
{
    /// <summary>Total of the moved-in minutes of the line.</summary>
    public static int Total(InvoiceLine line) => line.MovedIn.Sum(m => m.Minutes);

    /// <summary>
    /// Combines entries with the same date (summed, ordered by date), skips non-positive ones and, when given,
    /// the entries that come from <paramref name="excludeDate"/>.
    /// </summary>
    public static IReadOnlyList<InvoiceLineMove> Combine(IEnumerable<InvoiceLineMove> moves, DateOnly? excludeDate = null) =>
        moves
            .Where(m => m.Minutes > 0 && m.FromDate != excludeDate)
            .GroupBy(m => m.FromDate)
            .OrderBy(g => g.Key)
            .Select(g => new InvoiceLineMove(g.Key, g.Sum(m => m.Minutes)))
            .ToList();
}
