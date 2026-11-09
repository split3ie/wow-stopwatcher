using System.Security;

namespace WowStopwatcher.App.Features.Autostart;

/// <summary>Autostart through a LaunchAgent plist in <c>~/Library/LaunchAgents</c>.</summary>
public sealed class MacAutostart : IAutostartService
{
    private const string LaunchAgentLabel = "com.split3ie.wowstopwatcher";

    /// <summary>True when the plist exists.</summary>
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
        File.WriteAllText(file, BuildPlist(path));
    }

    private static string GetFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "LaunchAgents",
            $"{LaunchAgentLabel}.plist");

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
                <string>{AutostartCommand.MinimizedArgument}</string>
            </array>
            <key>RunAtLoad</key>
            <true/>
        </dict>
        </plist>

        """;
}
