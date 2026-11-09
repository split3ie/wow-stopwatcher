#if FEATURE_SETTINGS
using WowStopwatcher.App.Features.Settings;
#endif
#if FEATURE_TODAY
using WowStopwatcher.App.Features.Today;
#endif
#if FEATURE_AUTOSTART
using WowStopwatcher.App.Features.Autostart;
#endif
#if FEATURE_GOAL_NOTIFICATIONS
using WowStopwatcher.App.Features.GoalNotifications;
#endif
#if FEATURE_HOTKEYS
using WowStopwatcher.App.Features.Hotkeys;
#endif
#if FEATURE_STATISTICS
using WowStopwatcher.App.Features.Statistics;
#endif
#if FEATURE_TRAY
using WowStopwatcher.App.Features.Tray;
#endif
#if FEATURE_UPDATE_CHECK
using WowStopwatcher.App.Features.UpdateCheck;
#endif

using WowStopwatcher.Core;

namespace WowStopwatcher.App.Composition;

/// <summary>
/// The list of feature modules compiled into this build. This is the only place with <c>#if FEATURE_*</c>
/// (the flags are set in <c>Features.props</c>).
/// </summary>
public static class FeatureModules
{
    /// <summary>All modules of this build, in registration order.</summary>
    public static IReadOnlyList<IFeatureModule> All { get; } = Create();

    /// <summary>The stopwatch capabilities of this build (the Independent, Goals and Permanent flags).</summary>
    public static StopwatchCapabilities Capabilities { get; } = new(
#if FEATURE_INDEPENDENT
        Independent: true,
#else
        Independent: false,
#endif
#if FEATURE_GOALS
        Goals: true,
#else
        Goals: false,
#endif
#if FEATURE_PERMANENT
        Permanent: true);
#else
        Permanent: false);
#endif

    private static List<IFeatureModule> Create()
    {
        var modules = new List<IFeatureModule>();

        // Feature modules are added here, each under its own #if FEATURE_* block.
#if FEATURE_TODAY
        modules.Add(new TodayModule());
#endif
#if FEATURE_SETTINGS
        modules.Add(new SettingsModule());
#endif
#if FEATURE_HOTKEYS
        modules.Add(new HotkeysModule());
#endif
#if FEATURE_TRAY
        modules.Add(new TrayModule());
#endif
#if FEATURE_AUTOSTART
        modules.Add(new AutostartModule());
#endif
#if FEATURE_GOAL_NOTIFICATIONS
        modules.Add(new GoalNotificationsModule());
#endif
#if FEATURE_STATISTICS
        modules.Add(new StatisticsModule());
#endif
#if FEATURE_UPDATE_CHECK
        modules.Add(new UpdateCheckModule());
#endif
        return modules;
    }
}
