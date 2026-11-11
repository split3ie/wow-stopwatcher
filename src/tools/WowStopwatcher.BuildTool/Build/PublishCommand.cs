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

    /// <summary>
    /// Finds the output folder in the command tokens (<c>-o</c> / <c>--output</c> followed by a value, or the
    /// <c>-o:value</c>, <c>--output:value</c>, <c>--output=value</c> forms). A relative path is resolved against
    /// <paramref name="repoRoot"/>. Returns null when there is no output option.
    /// </summary>
    public static string? FindOutputDir(IReadOnlyList<string> tokens, string repoRoot)
    {
        string? value = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token is "-o" or "--output")
            {
                if (i + 1 < tokens.Count)
                {
                    value = tokens[i + 1];
                    i++;
                }
            }
            else
            {
                foreach (var prefix in new[] { "-o:", "--output:", "--output=" })
                {
                    if (token.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        value = token[prefix.Length..];
                        break;
                    }
                }
            }
        }

        return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value, repoRoot);
    }

    /// <summary>Formats the command for display, quoting tokens that contain spaces.</summary>
    public static string Format(IEnumerable<string> tokens) =>
        string.Join(' ', tokens.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
}
