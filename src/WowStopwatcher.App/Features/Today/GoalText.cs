using System.Globalization;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.Features.Today;

/// <summary>Text pieces shared by the Today row and the Goals card.</summary>
internal static class GoalText
{
    /// <summary>" · since Mon 20:00" when the open cycle started on an earlier day; otherwise empty.</summary>
    public static string Since(GoalStatus status, TimeZoneInfo zone) =>
        status.CarriedFromUtc is { } from
            ? " · since " + TimeZoneInfo.ConvertTime(from, zone).ToString("ddd HH:mm", CultureInfo.InvariantCulture)
            : string.Empty;

    /// <summary>The row sub-line: "Reached", "Reached ×N" or "N% done", plus the carried-over suffix.</summary>
    public static string Sub(GoalStatus status, TimeZoneInfo zone, double fraction)
    {
        var main = !status.IsReached
            ? $"{(int)Math.Floor(fraction * 100)}% done"
            : status.ReachedCount > 1 ? $"Reached ×{status.ReachedCount}" : "Reached";
        return main + Since(status, zone);
    }
}
