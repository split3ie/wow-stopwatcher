using System.Globalization;

namespace WowStopwatcher.Core.Invoicing;

/// <summary>A day start (minutes after midnight, 0 to 1439) as "HH:mm" text and back.</summary>
public static class DayStartText
{
    /// <summary>The largest valid day start: 23:59.</summary>
    public const int MaxMinutes = 24 * 60 - 1;

    /// <summary>Formats minutes after midnight as "HH:mm", for example 1200 as "20:00".</summary>
    public static string Format(int minutes) =>
        string.Create(CultureInfo.InvariantCulture, $"{minutes / 60:00}:{minutes % 60:00}");

    /// <summary>Parses "20:00", "8:30" or "8" into minutes after midnight; false for anything else or a time past 23:59.</summary>
    public static bool TryParse(string? text, out int minutes)
    {
        if (InvoiceTimeText.TryParse(text, out minutes) && minutes <= MaxMinutes)
        {
            return true;
        }

        minutes = 0;
        return false;
    }
}
