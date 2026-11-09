using System.Text.RegularExpressions;

namespace WowStopwatcher.BuildTool.Build;

/// <summary>Reads <c>Feature*</c> property names from Features.props and compares them with the catalog.</summary>
public static partial class FeaturePropsReader
{
    /// <summary>Returns the distinct <c>Feature*</c> element names found in the props file text.</summary>
    public static IReadOnlyList<string> ParseFlags(string propsXml) =>
        FlagRegex().Matches(propsXml).Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>Returns a warning text when the props file and the catalog differ, otherwise null.</summary>
    public static string? Compare(IReadOnlyList<string> propsFlags, IReadOnlyList<FeatureDefinition> catalog)
    {
        var missingInCatalog = propsFlags.Except(catalog.Select(f => f.Flag)).ToList();
        var missingInProps = catalog.Select(f => f.Flag).Except(propsFlags).ToList();
        if (missingInCatalog.Count == 0 && missingInProps.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        if (missingInCatalog.Count > 0)
        {
            parts.Add($"in Features.props but not in FeatureCatalog: {string.Join(", ", missingInCatalog)}");
        }

        if (missingInProps.Count > 0)
        {
            parts.Add($"in FeatureCatalog but not in Features.props: {string.Join(", ", missingInProps)}");
        }

        return "Feature list mismatch - " + string.Join("; ", parts) + ".";
    }

    [GeneratedRegex(@"<(Feature[A-Za-z0-9_]+)[\s>/]")]
    private static partial Regex FlagRegex();
}
