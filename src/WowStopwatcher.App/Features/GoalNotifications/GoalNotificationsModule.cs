using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Composition;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Features.GoalNotifications;

/// <summary>Goal-reached popup: the notifier (a background service) and the "Notify when a goal is reached" card in Settings.</summary>
public sealed class GoalNotificationsModule : IFeatureModule
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IGoalNotificationSettings, GoalNotificationSettings>();
        services.AddSingleton<IAppService, GoalNotifier>();
        services.AddTransient<ISettingsGeneralItem, GoalNotificationSettingsViewModel>();
        services.AddSingleton(new ViewMapping(typeof(GoalNotificationSettingsViewModel), () => new GoalNotificationSettingsView()));
    }
}
