using WowStopwatcher.Core;

namespace WowStopwatcher.App.Notifications;

/// <summary>The "notify when a goal is reached" setting: stored as "true"/"false", a missing value means enabled.</summary>
public static class GoalNotificationSetting
{
    public const string Key = "notify_goal_reached";

    public static bool IsEnabled(ISettingsRepository settings) => settings.Get(Key) != "false";

    public static void SetEnabled(ISettingsRepository settings, bool enabled) =>
        settings.Set(Key, enabled ? "true" : "false");
}
