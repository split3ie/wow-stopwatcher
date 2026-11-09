using WowStopwatcher.Core;

namespace WowStopwatcher.App.Features.GoalNotifications;

/// <summary><see cref="IGoalNotificationSettings"/> over the settings repository: stored as "true"/"false", a missing value means enabled.</summary>
public sealed class GoalNotificationSettings : IGoalNotificationSettings
{
    public const string Key = "notify_goal_reached";

    private readonly ISettingsRepository _settings;

    public GoalNotificationSettings(ISettingsRepository settings) => _settings = settings;

    public bool IsEnabled
    {
        get => _settings.Get(Key) != "false";
        set => _settings.Set(Key, value ? "true" : "false");
    }
}
