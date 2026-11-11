namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// One calendar month an invoice covers.
/// </summary>
public readonly record struct InvoicePeriod
{
    /// <summary>Creates a period; throws <see cref="ArgumentOutOfRangeException"/> when the year or month is invalid.</summary>
    public InvoicePeriod(int year, int month)
    {
        if (year is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year must be between 1 and 9999.");
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be between 1 and 12.");
        }

        Year = year;
        Month = month;
    }

    /// <summary>The year.</summary>
    public int Year { get; }

    /// <summary>The month, 1 to 12.</summary>
    public int Month { get; }

    /// <summary>First local day of the month.</summary>
    public DateOnly FirstDay => new(Year, Month, 1);

    /// <summary>Last local day of the month.</summary>
    public DateOnly LastDay => new(Year, Month, DateTime.DaysInMonth(Year, Month));

    /// <summary>Text key such as "2026-11".</summary>
    public string Key => $"{Year:0000}-{Month:00}";

    /// <summary>The period containing <paramref name="day"/>.</summary>
    public static InvoicePeriod From(DateOnly day) => new(day.Year, day.Month);

    /// <summary>True when <paramref name="day"/> lies in this month.</summary>
    public bool Contains(DateOnly day) => day.Year == Year && day.Month == Month;

    /// <summary>The half-open UTC range [start, end) of the month in <paramref name="zone"/>.</summary>
    public (DateTimeOffset StartUtc, DateTimeOffset EndUtc) RangeUtc(TimeZoneInfo zone) =>
        (LocalDays.StartOfDayUtc(FirstDay, zone), LocalDays.StartOfDayUtc(LastDay.AddDays(1), zone));

    /// <inheritdoc />
    public override string ToString() => Key;
}
