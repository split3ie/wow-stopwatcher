using Microsoft.Win32;

namespace WowStopwatcher.App.Services;

/// <summary>Registers the app to start with Windows (per user, HKCU Run key). Does nothing on other platforms.</summary>
public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WowStopwatcher";

    /// <summary>Command line argument that makes the app start hidden in the tray.</summary>
    public const string MinimizedArgument = "--minimized";

    /// <summary>True when autostart can be changed on this platform.</summary>
    public bool IsSupported => OperatingSystem.IsWindows() && !string.IsNullOrEmpty(Environment.ProcessPath);

    /// <summary>True when the Run key currently holds a WowStopwatcher entry.</summary>
    public bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string { Length: > 0 };
    }

    /// <summary>Adds or removes the Run entry. Throws if the registry cannot be written.</summary>
    public void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            var path = Environment.ProcessPath
                ?? throw new InvalidOperationException("The path of the running executable is unknown.");
            key.SetValue(ValueName, $"\"{path}\" {MinimizedArgument}", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
