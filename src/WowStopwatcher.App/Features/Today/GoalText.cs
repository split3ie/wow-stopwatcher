using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Today;

/// <summary>Text pieces shared by the Today row and the Goals card.</summary>
internal static class GoalText
{
    /// <summary>"text · since Mon 20:00" when the open cycle started on an earlier day; otherwise the text unchanged.</summary>
    public static string WithSince(string text, GoalStatus status, TimeZoneInfo zone) =>
        status.CarriedFromUtc is { } from
            ? Loc.T("today.goal.withSince", ("text", text),
                ("time", TimeZoneInfo.ConvertTime(from, zone).ToString(Loc.T("format.weekdayTime"), Loc.Culture)))
            : text;

    /// <summary>The row sub-line: "Reached", "Reached ×N" or "N% done", plus the carried-over suffix.</summary>
    public static string Sub(GoalStatus status, TimeZoneInfo zone, double fraction)
    {
        var main = !status.IsReached
            ? Loc.T("today.goal.done", ("percent", (int)Math.Floor(fraction * 100)))
            : status.ReachedCount > 1 ? Loc.T("today.goal.reachedTimes", ("count", status.ReachedCount)) : Loc.T("today.goal.reached");
        return WithSince(main, status, zone);
    }
}
