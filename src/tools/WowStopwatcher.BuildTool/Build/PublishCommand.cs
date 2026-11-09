namespace WowStopwatcher.BuildTool.Build;

/// <summary>Builds and formats the <c>dotnet publish</c> command line.</summary>
public static class PublishCommand
{
    /// <summary>Returns the full one-line publish command for one target.</summary>
    /// <param name="rid">Runtime identifier, for example <c>win-x64</c>.</param>
    /// <param name="outputDir">Folder that receives the published files.</param>
    /// <param name="disabledFlags">Feature flags to compile out.</param>
    /// <param name="version">Optional version; null or blank means do not pass it.</param>
    public static string BuildCommandLine(
        string rid, string outputDir, IEnumerable<string> disabledFlags, string? version)
    {
        var tokens = new List<string>
        {
            "dotnet", "publish", "src/WowStopwatcher.App", "-c", "Release", "-r", rid, "-o", outputDir,
        };
        tokens.AddRange(disabledFlags.Select(flag => $"-p:{flag}=false"));
        if (!string.IsNullOrWhiteSpace(version))
        {
            tokens.Add($"-p:Version={version.Trim()}");
        }

        return Format(tokens);
    }

    /// <summary>Formats the command for display, quoting tokens that contain spaces.</summary>
    public static string Format(IEnumerable<string> tokens) =>
        string.Join(' ', tokens.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
}
