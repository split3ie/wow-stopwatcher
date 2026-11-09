using System.Text.RegularExpressions;

namespace WowStopwatcher.BuildTool.Build;

/// <summary>Recognizes MSBuild/dotnet error lines in process output.</summary>
public static partial class BuildErrorFinder
{
    /// <summary>True for lines such as <c>x.cs(1,1): error CS0103: ...</c> or <c>MSBUILD : error MSB1001: ...</c>.</summary>
    public static bool IsErrorLine(string line) => ErrorRegex().IsMatch(line);

    [GeneratedRegex(@": error |\berror [A-Za-z]+\d+:", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorRegex();
}
