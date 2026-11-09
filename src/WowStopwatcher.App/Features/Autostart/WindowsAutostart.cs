using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WowStopwatcher.App.Features.Autostart;

/// <summary>Autostart through the HKCU Run key.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutostart : IAutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WowStopwatcher";

    /// <summary>True when the Run key entry exists.</summary>
    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string { Length: > 0 };
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{AutostartCommand.GetProcessPath()}\" {AutostartCommand.MinimizedArgument}", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
