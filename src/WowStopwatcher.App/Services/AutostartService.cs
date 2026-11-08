using System.Security;
using Microsoft.Win32;

namespace WowStopwatcher.App.Services;

/// <summary>
/// Registers the app to start at login, per user: HKCU Run key on Windows, a LaunchAgent plist on macOS and an
/// XDG autostart .desktop file on Linux.
/// </summary>
public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WowStopwatcher";
    private const string LaunchAgentLabel = "com.split3ie.wowstopwatcher";

    /// <summary>Command line argument that makes the app start hidden in the tray.</summary>
    public const string MinimizedArgument = "--minimized";

    /// <summary>True when autostart can be changed on this platform.</summary>
    public bool IsSupported =>
        (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        && !string.IsNullOrEmpty(Environment.ProcessPath);

    /// <summary>True when the Run key entry (Windows) or the autostart file (macOS, Linux) exists.</summary>
    public bool IsEnabled()
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string { Length: > 0 };
        }

        var file = GetAutostartFilePath();
        return file is not null && File.Exists(file);
    }

    /// <summary>Adds or removes the autostart entry. Throws if it cannot be written.</summary>
    public void SetEnabled(bool enabled)
    {
        if (OperatingSystem.IsWindows())
        {
            SetWindowsEnabled(enabled);
            return;
        }

        var file = GetAutostartFilePath();
        if (file is null)
        {
            return;
        }

        if (!enabled)
        {
            File.Delete(file);
            return;
        }

        var path = GetProcessPath();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, OperatingSystem.IsMacOS() ? BuildPlist(path) : BuildDesktopEntry(path));
    }

    private static void SetWindowsEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{GetProcessPath()}\" {MinimizedArgument}", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string GetProcessPath() =>
        Environment.ProcessPath
        ?? throw new InvalidOperationException("The path of the running executable is unknown.");

    private static string? GetAutostartFilePath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "LaunchAgents", $"{LaunchAgentLabel}.plist");
        }

        if (OperatingSystem.IsLinux())
        {
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(configHome) || !Path.IsPathRooted(configHome))
            {
                configHome = Path.Combine(home, ".config");
            }

            return Path.Combine(configHome, "autostart", "wowstopwatcher.desktop");
        }

        return null;
    }

    private static string BuildPlist(string path) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>Label</key>
            <string>{LaunchAgentLabel}</string>
            <key>ProgramArguments</key>
            <array>
                <string>{SecurityElement.Escape(path)}</string>
                <string>{MinimizedArgument}</string>
            </array>
            <key>RunAtLoad</key>
            <true/>
        </dict>
        </plist>

        """;

    private static string BuildDesktopEntry(string path)
    {
        // Desktop Entry spec: quote the argument and escape ", `, $ and \ with a backslash; the backslash
        // itself is then doubled again by the key value escaping rules. '%' must be written as '%%'.
        var quoted = path
            .Replace("\\", "\\\\\\\\")
            .Replace("\"", "\\\\\"")
            .Replace("`", "\\\\`")
            .Replace("$", "\\\\$")
            .Replace("%", "%%");

        return $"""
            [Desktop Entry]
            Type=Application
            Name=WowStopwatcher
            Exec="{quoted}" {MinimizedArgument}
            X-GNOME-Autostart-enabled=true

            """;
    }
}
