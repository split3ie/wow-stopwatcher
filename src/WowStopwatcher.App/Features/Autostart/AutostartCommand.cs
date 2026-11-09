namespace WowStopwatcher.App.Features.Autostart;

/// <summary>What the autostart entries run: this executable, started hidden in the tray.</summary>
internal static class AutostartCommand
{
    /// <summary>Command line argument that makes the app start hidden in the tray.</summary>
    public const string MinimizedArgument = "--minimized";

    /// <summary>True when the path of the running executable is known, so an entry can be written.</summary>
    public static bool HasProcessPath => !string.IsNullOrEmpty(Environment.ProcessPath);

    public static string GetProcessPath() =>
        Environment.ProcessPath
        ?? throw new InvalidOperationException("The path of the running executable is unknown.");
}
