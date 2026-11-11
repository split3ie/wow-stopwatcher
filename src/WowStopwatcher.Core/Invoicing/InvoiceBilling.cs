namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// Only allocated time is billed: a line with a non-empty note. Lines without a note are unallocated; they stay visible
/// but are left out of everything billed.
/// </summary>
public static class InvoiceBilling
{
    /// <summary>True when the line has a note (ignoring white space) and so is billed.</summary>
    public static bool IsBilled(InvoiceLine line) => !string.IsNullOrWhiteSpace(line.Note);

    /// <summary>The billed lines of the days, in order.</summary>
    public static IReadOnlyList<InvoiceLine> BilledLines(IEnumerable<InvoiceDay> days) =>
        days.SelectMany(d => d.Lines).Where(IsBilled).ToList();

    /// <summary>Total minutes of the billed lines.</summary>
    public static int BilledMinutes(IEnumerable<InvoiceDay> days) => BilledLines(days).Sum(l => l.Minutes);

    /// <summary>Total minutes of the lines without a note.</summary>
    public static int UnallocatedMinutes(IEnumerable<InvoiceDay> days) =>
        days.SelectMany(d => d.Lines).Where(l => !IsBilled(l)).Sum(l => l.Minutes);
}
