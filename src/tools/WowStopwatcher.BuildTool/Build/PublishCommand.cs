namespace WowStopwatcher.BuildTool.Build;

/// <summary>Builds the <c>dotnet publish</c> arguments for one target.</summary>
public static class PublishCommand
{
    /// <summary>Returns the argument list (without the <c>dotnet</c> executable).</summary>
    /// <param name="repoRoot">Repository root.</param>
    /// <param name="rid">Runtime identifier, for example <c>win-x64</c>.</param>
    /// <param name="outputDir">Folder that receives the published files.</param>
    /// <param name="disabledFlags">Feature flags to compile out.</param>
    /// <param name="version">Optional version; null or blank means do not pass it.</param>
    public static IReadOnlyList<string> BuildArguments(
        string repoRoot, string rid, string outputDir, IEnumerable<string> disabledFlags, string? version)
    {
        var args = new List<string>
        {
            "publish",
            Path.Combine(repoRoot, "src", "WowStopwatcher.App"),
            "-c", "Release",
            "-r", rid,
            "-o", outputDir,
        };
        args.AddRange(disabledFlags.Select(flag => $"-p:{flag}=false"));
        if (!string.IsNullOrWhiteSpace(version))
        {
            args.Add($"-p:Version={version.Trim()}");
        }

        return args;
    }

    /// <summary>Formats the command for display in the log, quoting arguments that contain spaces.</summary>
    public static string Format(IEnumerable<string> arguments) =>
        "dotnet " + string.Join(' ', arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
}
