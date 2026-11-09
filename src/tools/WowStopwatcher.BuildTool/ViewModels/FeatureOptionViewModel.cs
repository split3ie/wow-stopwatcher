using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.BuildTool.Build;

namespace WowStopwatcher.BuildTool.ViewModels;

/// <summary>A feature checkbox; its state comes from <see cref="FeatureSelection"/>.</summary>
public sealed class FeatureOptionViewModel(FeatureDefinition definition, FeatureSelection selection, Action changed)
    : ObservableObject
{
    public FeatureDefinition Definition { get; } = definition;

    public string Title => Definition.DisplayName;

    /// <summary>Description, or "Requires X" while a requirement is off.</summary>
    public string DescriptionText => RequiresText ?? Definition.Description;

    public string Label => $"{Definition.DisplayName} - {Definition.Description}";

    /// <summary>Checked state shown to the user; false while a requirement is off.</summary>
    public bool IsEnabled
    {
        get => selection.IsEffective(Definition.Flag);
        set
        {
            selection.SetChosen(Definition.Flag, value);
            changed();
        }
    }

    /// <summary>False when a required feature is off.</summary>
    public bool IsAvailable => selection.IsAvailable(Definition.Flag);

    /// <summary>"Requires X" while a requirement is off, otherwise null.</summary>
    public string? RequiresText
    {
        get
        {
            var missing = selection.MissingRequirement(Definition.Flag);
            return missing is null ? null : "Requires " + missing;
        }
    }

    /// <summary>Raises change notifications after any selection change.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(RequiresText));
        OnPropertyChanged(nameof(DescriptionText));
    }
}
