namespace WowStopwatcher.BuildTool.Build;

/// <summary>A titled group of related features shown as one section.</summary>
/// <param name="Id">Stable identifier, for example <c>Pages</c>.</param>
/// <param name="Title">Section title.</param>
/// <param name="RequireAtLeastOne">When true, at least one member must stay selected for a build.</param>
/// <param name="AtLeastOneMessage">Error shown when <paramref name="RequireAtLeastOne"/> is violated.</param>
public sealed record FeatureGroup(
    string Id, string Title, bool RequireAtLeastOne = false, string? AtLeastOneMessage = null);
