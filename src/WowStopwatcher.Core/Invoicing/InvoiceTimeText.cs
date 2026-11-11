using System.Globalization;

namespace WowStopwatcher.Core.Invoicing;

/// <summary>Minutes as "h:mm" text and back, as typed and shown in the invoice time dialog.</summary>
public static class InvoiceTimeText
{
    private const int MaxHours = 999;

    /// <summary>Formats minutes as "h:mm", for example 483 as "8:03".</summary>
    public static string Format(int minutes) =>
        string.Create(CultureInfo.InvariantCulture, $"{minutes / 60}:{minutes % 60:00}");

    /// <summary>Formats minutes as "hhh:mm" (hours padded to 3 digits), for example 483 as "008:03", as shown in the fixed-shape time boxes.</summary>
    public static string FormatPadded(int minutes) =>
        string.Create(CultureInfo.InvariantCulture, $"{minutes / 60:000}:{minutes % 60:00}");

    /// <summary>Formats minutes as "hh:mm" for the two-digit-hours time boxes; anything over 99:59 is clamped to 99:59.</summary>
    public static string FormatShort(int minutes) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Min(minutes, 99 * 60 + 59) / 60:00}:{Math.Min(minutes, 99 * 60 + 59) % 60:00}");

    /// <summary>
    /// Parses "8", "8:00" or "0:45" (hours up to 3 digits, minutes 1 or 2 digits and at most 59) into minutes.
    /// Returns false for anything else, including an empty text.
    /// </summary>
    public static bool TryParse(string? text, out int minutes)
    {
        minutes = 0;
        var parts = (text ?? string.Empty).Trim().Split(':');
        if (parts.Length is < 1 or > 2
            || !TryPart(parts[0], 3, out var hours)
            || hours > MaxHours)
        {
            return false;
        }

        var rest = 0;
        if (parts.Length == 2 && (!TryPart(parts[1], 2, out rest) || rest > 59))
        {
            return false;
        }

        minutes = hours * 60 + rest;
        return true;
    }

    private static bool TryPart(string part, int maxDigits, out int value)
    {
        value = 0;
        return part.Length >= 1 && part.Length <= maxDigits
            && part.All(char.IsAsciiDigit)
            && int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
