using System.Globalization;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.Core;

/// <summary>Text formatting and parsing of durations. Negative durations are treated as zero.</summary>
public static class TimeFormat
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>"HH:MM:SS" or "HH:MM:SS.mmm". Hours can exceed 99; parts are truncated, not rounded.</summary>
    public static string Clock(TimeSpan value, bool withMs = false)
    {
        value = NonNegative(value);
        var hours = value.Ticks / TimeSpan.TicksPerHour;
        var text = string.Format(Invariant, "{0:00}:{1:00}:{2:00}", hours, value.Minutes, value.Seconds);
        return withMs ? text + string.Format(Invariant, ".{0:000}", value.Milliseconds) : text;
    }

    /// <summary>"1h 2m 3s", "2m 3s" or "3s" (seconds truncated).</summary>
    public static string Human(TimeSpan value)
    {
        value = NonNegative(value);
        var hours = value.Ticks / TimeSpan.TicksPerHour;
        if (hours > 0)
        {
            return Loc.T("time.hoursMinutesSeconds", ("hours", hours), ("minutes", value.Minutes), ("seconds", value.Seconds));
        }

        return value.Minutes > 0
            ? Loc.T("time.minutesSeconds", ("minutes", value.Minutes), ("seconds", value.Seconds))
            : Loc.T("time.seconds", ("seconds", value.Seconds));
    }

    /// <summary>"1h 30m", "1h" or "30m", rounded to the nearest minute ("0m" below 30 seconds).</summary>
    public static string HoursMinutes(TimeSpan value)
    {
        value = NonNegative(value);
        var totalMinutes = (long)Math.Round(value.TotalMinutes, MidpointRounding.AwayFromZero);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours > 0 && minutes > 0)
        {
            return Loc.T("time.hoursMinutes", ("hours", hours), ("minutes", minutes));
        }

        return hours > 0
            ? Loc.T("time.hours", ("hours", hours))
            : Loc.T("time.minutes", ("minutes", minutes));
    }

    /// <summary>
    /// Parses a goal typed as "hh:mm:ss" (hours 1-4 digits, minutes and seconds 1-2 digits below 60).
    /// Returns false for empty, malformed or zero input.
    /// </summary>
    public static bool TryParseGoal(string? text, out TimeSpan goal)
    {
        goal = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Split(':');
        if (parts.Length != 3
            || !TryParsePart(parts[0], 4, out var hours)
            || !TryParsePart(parts[1], 2, out var minutes)
            || !TryParsePart(parts[2], 2, out var seconds)
            || minutes >= 60
            || seconds >= 60)
        {
            return false;
        }

        var value = new TimeSpan(hours, minutes, seconds);
        if (value <= TimeSpan.Zero)
        {
            return false;
        }

        goal = value;
        return true;
    }

    /// <summary>Goal as "hh:mm:ss", for example "05:00:00".</summary>
    public static string FormatGoal(TimeSpan goal) => Clock(goal, withMs: false);

    /// <summary>Short goal label: "5h", "45m", "1h 30m" (seconds only when below one minute, e.g. "30s").</summary>
    public static string GoalLabel(TimeSpan goal)
    {
        goal = NonNegative(goal);
        return goal > TimeSpan.Zero && goal < TimeSpan.FromMinutes(1)
            ? Loc.T("time.seconds", ("seconds", goal.Seconds))
            : HoursMinutes(goal);
    }

    /// <summary>English ordinal: 1st, 2nd, 3rd, 4th, 11th, 12th, 13th, 21st, 22nd, 101st...</summary>
    public static string Ordinal(int number)
    {
        var abs = Math.Abs(number);
        var suffix = (abs % 100) is >= 11 and <= 13
            ? "th"
            : (abs % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            };
        return number.ToString(Invariant) + suffix;
    }

    private static TimeSpan NonNegative(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;

    private static bool TryParsePart(string part, int maxDigits, out int value)
    {
        value = 0;
        return part.Length >= 1
            && part.Length <= maxDigits
            && part.All(char.IsAsciiDigit)
            && int.TryParse(part, NumberStyles.None, Invariant, out value);
    }
}
