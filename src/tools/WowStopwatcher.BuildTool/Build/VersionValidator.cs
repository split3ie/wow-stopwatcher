using System.Text.RegularExpressions;

namespace WowStopwatcher.BuildTool.Build;

/// <summary>Checks the optional version text before it is passed as <c>-p:Version</c>.</summary>
public static partial class VersionValidator
{
    /// <summary>Message shown under the Version box when the text is invalid.</summary>
    public const string ErrorText = "Use a version like 1.2.3 or 1.2.3-beta";

    /// <summary>True for empty text, MAJOR.MINOR or MAJOR.MINOR.PATCH with an optional prerelease suffix.</summary>
    public static bool IsValid(string? version) =>
        string.IsNullOrWhiteSpace(version) || VersionRegex().IsMatch(version.Trim());

    /// <summary>Returns <see cref="ErrorText"/> when the version is invalid, otherwise null.</summary>
    public static string? Validate(string? version) => IsValid(version) ? null : ErrorText;

    [GeneratedRegex(@"^\d+\.\d+(\.\d+)?(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$")]
    private static partial Regex VersionRegex();
}
