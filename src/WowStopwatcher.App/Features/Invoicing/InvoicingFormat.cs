using System.Globalization;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Text formatting and parsing of the Invoicing page: durations, money, months and the hourly rate.</summary>
internal static class InvoicingFormat
{
    /// <summary>"1h 30m" like the rest of the app.</summary>
    public static string Duration(int minutes) => TimeFormat.HoursMinutes(TimeSpan.FromMinutes(minutes));

    /// <summary>"1 234.50 USD" (digit grouping follows the app language).</summary>
    public static string Money(decimal amount, string currency) => amount.ToString("N2", Loc.Culture) + " " + currency;

    /// <summary>The month name and year in the app language, first letter upper case.</summary>
    public static string Month(InvoicePeriod period)
    {
        var text = period.FirstDay.ToString("MMMM yyyy", Loc.Culture);
        return text.Length == 0 ? text : char.ToUpper(text[0], Loc.Culture) + text[1..];
    }

    /// <summary>The rate for editing: no thousands separators, up to 2 decimals, invariant.</summary>
    public static string Rate(decimal rate) => rate.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Parses a non-negative decimal typed with a dot or a comma.</summary>
    public static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0;
        return !string.IsNullOrWhiteSpace(text)
            && decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value)
            && value >= 0;
    }
}
