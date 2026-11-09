namespace WowStopwatcher.App.Features.Autostart;

/// <summary>Autostart through an XDG autostart .desktop file.</summary>
public sealed class LinuxAutostart : IAutostartService
{
    /// <summary>True when the .desktop file exists.</summary>
    public bool IsEnabled() => File.Exists(GetFilePath());

    public void SetEnabled(bool enabled)
    {
        var file = GetFilePath();
        if (!enabled)
        {
            File.Delete(file);
            return;
        }

        var path = AutostartCommand.GetProcessPath();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, BuildDesktopEntry(path));
    }

    private static string GetFilePath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(configHome) || !Path.IsPathRooted(configHome))
        {
            configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(configHome, "autostart", "wowstopwatcher.desktop");
    }

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
            Exec="{quoted}" {AutostartCommand.MinimizedArgument}
            X-GNOME-Autostart-enabled=true

            """;
    }
}
