using System.Reflection;

namespace WowStopwatcher.App.Shell;

/// <summary>Reads the version from the entry assembly.</summary>
public sealed class AppVersion : IAppVersion
{
    public AppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly;
        var text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        var plus = text.IndexOf('+');
        Version = plus >= 0 ? text[..plus] : text;

        var official = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(a => a.Key == "OfficialBuild" && a.Value == "true");
        DisplayText = official ? "v" + Version : "local build";
    }

    public string Version { get; }

    public string DisplayText { get; }
}
