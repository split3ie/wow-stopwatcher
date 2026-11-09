namespace WowStopwatcher.BuildTool.Build;

/// <summary>Creates and ad-hoc signs <c>WowStopwatcher.app</c> the way release.yml does. Works on macOS only.</summary>
public static class MacAppBundler
{
    /// <summary>Builds the bundle next to the published binary and returns false if signing failed.</summary>
    public static async Task<bool> CreateAsync(
        string repoRoot, string outputDir, string version, Action<string> onLine, CancellationToken cancellation)
    {
        var app = Path.Combine(outputDir, "WowStopwatcher.app");
        if (Directory.Exists(app))
        {
            Directory.Delete(app, recursive: true);
        }

        var macOsDir = Path.Combine(app, "Contents", "MacOS");
        var resourcesDir = Path.Combine(app, "Contents", "Resources");
        Directory.CreateDirectory(macOsDir);
        Directory.CreateDirectory(resourcesDir);

        var template = Path.Combine(repoRoot, "build", "macos");
        File.Copy(Path.Combine(template, "WowStopwatcher.icns"), Path.Combine(resourcesDir, "WowStopwatcher.icns"));

        var binary = Path.Combine(macOsDir, "WowStopwatcher");
        File.Copy(Path.Combine(outputDir, "WowStopwatcher"), binary);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(binary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        var plist = File.ReadAllText(Path.Combine(template, "Info.plist")).Replace("@VERSION@", version);
        File.WriteAllText(Path.Combine(app, "Contents", "Info.plist"), plist);

        onLine("codesign --force --deep -s - " + app);
        var exit = await ProcessRunner.RunAsync(
            "codesign", ["--force", "--deep", "-s", "-", app], outputDir, onLine, cancellation);
        return exit == 0;
    }
}
