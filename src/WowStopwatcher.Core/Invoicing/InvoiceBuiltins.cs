using System.Globalization;

namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// The built-in template values. Numbers use the invariant culture ("0.00"); dates use the given culture.
/// </summary>
public static class InvoiceBuiltins
{
    /// <summary>Total hours.</summary>
    public const string Hours = "hours";

    /// <summary>Total minutes.</summary>
    public const string Minutes = "minutes";

    /// <summary>Hourly rate.</summary>
    public const string Rate = "rate";

    /// <summary>Total amount.</summary>
    public const string Amount = "amount";

    /// <summary>Currency code.</summary>
    public const string Currency = "currency";

    /// <summary>Period as "yyyy-MM".</summary>
    public const string PeriodMonth = "period.month";

    /// <summary>Period as "MMMM yyyy".</summary>
    public const string PeriodName = "period.name";

    /// <summary>First day of the period.</summary>
    public const string PeriodStart = "period.start";

    /// <summary>Last day of the period.</summary>
    public const string PeriodEnd = "period.end";

    /// <summary>Invoice number; empty when not assigned yet.</summary>
    public const string InvoiceNumber = "invoice.number";

    /// <summary>Today's date.</summary>
    public const string Today = "today";

    /// <summary>Company name.</summary>
    public const string CompanyName = "company.name";

    /// <summary>Line date.</summary>
    public const string LineDate = "line.date";

    /// <summary>Line stopwatch names.</summary>
    public const string LineStopwatch = "line.stopwatch";

    /// <summary>Line time as "H:mm".</summary>
    public const string LineTime = "line.time";

    /// <summary>Line hours.</summary>
    public const string LineHours = "line.hours";

    /// <summary>Line amount.</summary>
    public const string LineAmount = "line.amount";

    /// <summary>Line note.</summary>
    public const string LineNote = "line.note";

    /// <summary>Every built-in name.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Hours, Minutes, Rate, Amount, Currency, PeriodMonth, PeriodName, PeriodStart, PeriodEnd, InvoiceNumber, Today, CompanyName,
        LineDate, LineStopwatch, LineTime, LineHours, LineAmount, LineNote,
    ];

    /// <summary>The names whose value is money and is masked while amounts are hidden.</summary>
    public static readonly IReadOnlyList<string> MoneyNames = [Rate, Amount, LineAmount];

    private static readonly HashSet<string> Names = new(All, StringComparer.Ordinal);

    /// <summary>True when <paramref name="name"/> is a built-in name.</summary>
    public static bool IsBuiltin(string name) => Names.Contains(name);

    /// <summary>True when <paramref name="name"/> is a per-line marker (starts with "line.").</summary>
    public static bool IsLineMarker(string name) => name.StartsWith("line.", StringComparison.Ordinal);

    /// <summary>Creates the invoice-level values (every built-in that is not a line marker).</summary>
    public static Dictionary<string, string> Create(
        int totalMinutes,
        decimal rate,
        string currency,
        string companyName,
        InvoicePeriod period,
        int? invoiceNumber,
        DateOnly today,
        CultureInfo culture)
    {
        var shortDate = culture.DateTimeFormat.ShortDatePattern;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Hours] = Number(InvoiceMath.Hours(totalMinutes)),
            [Minutes] = totalMinutes.ToString(CultureInfo.InvariantCulture),
            [Rate] = Number(rate),
            [Amount] = Number(InvoiceMath.Amount(totalMinutes, rate)),
            [Currency] = currency,
            [PeriodMonth] = period.Key,
            [PeriodName] = period.FirstDay.ToString("MMMM yyyy", culture),
            [PeriodStart] = period.FirstDay.ToString(shortDate, culture),
            [PeriodEnd] = period.LastDay.ToString(shortDate, culture),
            [InvoiceNumber] = invoiceNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            [Today] = today.ToString(shortDate, culture),
            [CompanyName] = companyName,
        };
    }

    /// <summary>Creates the per-line values for <paramref name="line"/>.</summary>
    public static Dictionary<string, string> CreateLine(InvoiceLine line, decimal rate, CultureInfo culture) =>
        new(StringComparer.Ordinal)
        {
            [LineDate] = line.Date.ToString(culture.DateTimeFormat.ShortDatePattern, culture),
            [LineStopwatch] = string.Join(", ", line.Sources.Select(s => s.StopwatchName)),
            [LineTime] = $"{line.Minutes / 60}:{line.Minutes % 60:00}",
            [LineHours] = Number(InvoiceMath.Hours(line.Minutes)),
            [LineAmount] = Number(InvoiceMath.Amount(line.Minutes, rate)),
            [LineNote] = line.Note,
        };

    private static string Number(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
