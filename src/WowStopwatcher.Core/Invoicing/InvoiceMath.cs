namespace WowStopwatcher.Core.Invoicing;

/// <summary>Hours and money from billed minutes.</summary>
public static class InvoiceMath
{
    /// <summary>Minutes as hours, rounded to 2 decimals (away from zero).</summary>
    public static decimal Hours(int minutes) => Math.Round(minutes / 60m, 2, MidpointRounding.AwayFromZero);

    /// <summary>Amount for <paramref name="minutes"/> at the hourly <paramref name="rate"/>, rounded to 2 decimals (away from zero).</summary>
    public static decimal Amount(int minutes, decimal rate) =>
        Math.Round(minutes * rate / 60m, 2, MidpointRounding.AwayFromZero);
}
