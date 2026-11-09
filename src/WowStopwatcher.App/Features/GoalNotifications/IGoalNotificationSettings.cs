namespace WowStopwatcher.App.Features.GoalNotifications;

/// <summary>The "notify when a goal is reached" setting.</summary>
public interface IGoalNotificationSettings
{
    /// <summary>True when the popup is shown. On by default.</summary>
    bool IsEnabled { get; set; }
}
