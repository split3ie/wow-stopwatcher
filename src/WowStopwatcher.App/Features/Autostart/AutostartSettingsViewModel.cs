using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Features.Autostart;

/// <summary>The "Run with computer start" card in the General section of Settings.</summary>
public sealed partial class AutostartSettingsViewModel : ObservableObject, ISettingsGeneralItem
{
    private readonly IAutostartService _autostart;
    private bool _syncing;

    public AutostartSettingsViewModel(IAutostartService autostart)
    {
        _autostart = autostart;
        try
        {
            _isEnabled = _autostart.IsEnabled();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Error = "Couldn't read the autostart setting";
        }
    }

    public int Order => 0;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string _error = string.Empty;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_syncing)
        {
            return;
        }

        try
        {
            _autostart.SetEnabled(value);
            Error = string.Empty;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException
            or InvalidOperationException)
        {
            // Show the real state again instead of a switch that does nothing.
            _syncing = true;
            IsEnabled = !value;
            _syncing = false;
            Error = "Couldn't change the autostart setting";
        }
    }
}
