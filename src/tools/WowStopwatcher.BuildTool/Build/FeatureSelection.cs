namespace WowStopwatcher.BuildTool.Build;

/// <summary>
/// The user's feature choices plus the dependency and group rules, with no UI.
/// A feature is effective when it is chosen and every flag it requires is effective.
/// The user's choice is kept while a dependency is off, so it comes back when the dependency is re-enabled.
/// The catalog must not contain dependency cycles.
/// </summary>
public sealed class FeatureSelection
{
    private readonly IReadOnlyList<FeatureDefinition> _features;
    private readonly Dictionary<string, FeatureDefinition> _byFlag;
    private readonly Dictionary<string, bool> _chosen;

    /// <summary>Creates a selection with every feature chosen.</summary>
    public FeatureSelection(IReadOnlyList<FeatureDefinition> features)
    {
        _features = features;
        _byFlag = features.ToDictionary(f => f.Flag);
        _chosen = features.ToDictionary(f => f.Flag, _ => true);
    }

    /// <summary>The user's own choice, ignoring dependencies.</summary>
    public bool IsChosen(string flag) => _chosen[flag];

    /// <summary>Records the user's choice for a feature.</summary>
    public void SetChosen(string flag, bool value) => _chosen[flag] = value;

    /// <summary>True when every required flag is effective.</summary>
    public bool IsAvailable(string flag) => MissingRequirement(flag) is null;

    /// <summary>True when the feature is chosen and available, so it is built in.</summary>
    public bool IsEffective(string flag) => _chosen[flag] && IsAvailable(flag);

    /// <summary>The display name of the first required feature that is not effective, or null.</summary>
    public string? MissingRequirement(string flag)
    {
        foreach (var required in _byFlag[flag].RequiredFlags)
        {
            if (!_byFlag.TryGetValue(required, out var definition))
            {
                return required;
            }

            if (!IsEffective(required))
            {
                return definition.DisplayName;
            }
        }

        return null;
    }

    /// <summary>True when all available members of the group are on, false when none are, null when mixed.</summary>
    public bool? GroupState(FeatureGroup group)
    {
        var members = AvailableMembers(group);
        var on = members.Count(f => IsEffective(f.Flag));
        if (members.Count == 0 || on == 0)
        {
            return false;
        }

        return on == members.Count ? true : null;
    }

    /// <summary>Checks or unchecks every currently available member of a group.</summary>
    public void SetGroup(FeatureGroup group, bool value)
    {
        foreach (var member in AvailableMembers(group))
        {
            _chosen[member.Flag] = value;
        }
    }

    /// <summary>The error for a group that requires at least one member but has none on, otherwise null.</summary>
    public string? GroupError(FeatureGroup group) =>
        group.RequireAtLeastOne && !_features.Any(f => f.Group == group && IsEffective(f.Flag))
            ? group.AtLeastOneMessage ?? $"Select at least one in {group.Title}"
            : null;

    /// <summary>Error messages of all groups that currently fail their rule.</summary>
    public IReadOnlyList<string> GroupErrors() =>
        _features.Select(f => f.Group).Distinct()
            .Select(GroupError).OfType<string>().ToList();

    /// <summary>Flags to pass as <c>=false</c>: unchecked ones and ones disabled by a dependency.</summary>
    public IReadOnlyList<string> DisabledFlags() =>
        _features.Where(f => !IsEffective(f.Flag)).Select(f => f.Flag).ToList();

    private List<FeatureDefinition> AvailableMembers(FeatureGroup group) =>
        _features.Where(f => f.Group == group && IsAvailable(f.Flag)).ToList();
}
