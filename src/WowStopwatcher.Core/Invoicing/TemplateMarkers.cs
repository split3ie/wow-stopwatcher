using System.Text.RegularExpressions;

namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// Finds and replaces <c>{{name}}</c> markers; the name is made of letters, digits, '_' and '.', and inner spaces are allowed.
/// </summary>
public static partial class TemplateMarkers
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_.]+)\s*\}\}")]
    private static partial Regex MarkerRegex();

    /// <summary>The distinct marker names in order of first appearance.</summary>
    public static IReadOnlyList<string> Find(string text) =>
        MarkerRegex().Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>Replaces each marker with <paramref name="resolve"/>'s result; a marker for which it returns null is left untouched.</summary>
    public static string Replace(string text, Func<string, string?> resolve) =>
        MarkerRegex().Replace(text, m => resolve(m.Groups[1].Value) ?? m.Value);
}
