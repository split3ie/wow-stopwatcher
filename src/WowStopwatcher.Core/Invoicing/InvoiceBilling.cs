namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// Only allocated time is billed: a line with a non-empty note or one marked as logged in an external time tracker.
/// Other lines are unallocated; they stay visible but are left out of everything billed.
/// </summary>
public static class InvoiceBilling
{
    /// <summary>True when the line has a note (ignoring white space) or is logged, and so is billed.</summary>
    public static bool IsBilled(InvoiceLine line) => line.Logged || !string.IsNullOrWhiteSpace(line.Note);

    /// <summary>The billed lines of the days, in order.</summary>
    public static IReadOnlyList<InvoiceLine> BilledLines(IEnumerable<InvoiceDay> days) =>
        days.SelectMany(d => d.Lines).Where(IsBilled).ToList();

    /// <summary>Total minutes of the billed lines.</summary>
    public static int BilledMinutes(IEnumerable<InvoiceDay> days) => BilledLines(days).Sum(l => l.Minutes);

    /// <summary>Largest reported time: 999:59, the same as <see cref="InvoiceTimeText"/> accepts.</summary>
    public const int MaxReportedMinutes = 999 * 60 + 59;

    /// <summary>True when <paramref name="minutes"/> is a valid reported time (1 to <see cref="MaxReportedMinutes"/>).</summary>
    public static bool IsValidReportedMinutes(int minutes) => minutes is >= 1 and <= MaxReportedMinutes;

    /// <summary>The minutes the invoice bills: the reported time when set, otherwise the total of the billed lines.</summary>
    public static int InvoicedMinutes(IEnumerable<InvoiceDay> days, int? reportedMinutes) => reportedMinutes ?? BilledMinutes(days);

    /// <summary>Total minutes of the lines that are not billed (no note and not logged).</summary>
    public static int UnallocatedMinutes(IEnumerable<InvoiceDay> days) =>
        days.SelectMany(d => d.Lines).Where(l => !IsBilled(l)).Sum(l => l.Minutes);
}
