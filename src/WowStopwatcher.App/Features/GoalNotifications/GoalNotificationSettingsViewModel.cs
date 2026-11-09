using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Features.GoalNotifications;

/// <summary>The "Notify when a goal is reached" card in the General section of Settings.</summary>
public sealed partial class GoalNotificationSettingsViewModel : ObservableObject, ISettingsGeneralItem
{
    private readonly IGoalNotificationSettings _settings;

    public GoalNotificationSettingsViewModel(IGoalNotificationSettings settings)
    {
        _settings = settings;
        _isEnabled = settings.IsEnabled;
    }

    public int Order => 10;

    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value) => _settings.IsEnabled = value;
}
