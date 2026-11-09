namespace WowStopwatcher.BuildTool.Build;

/// <summary>One build-time feature flag of the app.</summary>
/// <param name="Flag">MSBuild property name in Features.props, for example <c>FeatureTray</c>.</param>
/// <param name="DisplayName">Name shown next to the checkbox.</param>
/// <param name="Description">One-line description.</param>
/// <param name="Group">The section this feature belongs to.</param>
/// <param name="Requires">Flags that must be on for this feature to be available; null means none.</param>
public sealed record FeatureDefinition(
    string Flag, string DisplayName, string Description, FeatureGroup Group, IReadOnlyList<string>? Requires = null)
{
    /// <summary>The flags this feature depends on (never null).</summary>
    public IReadOnlyList<string> RequiredFlags => Requires ?? [];
}
