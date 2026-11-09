using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.BuildTool.Build;

namespace WowStopwatcher.BuildTool.ViewModels;

/// <summary>A navigation page of feature toggles with a three-state group checkbox.</summary>
public sealed class FeatureGroupViewModel(
    FeatureGroup group, FeatureSelection selection, ObservableCollection<FeatureOptionViewModel> options, Action changed)
    : NavPageViewModel
{
    public override string Title => group.Title;

    public ObservableCollection<FeatureOptionViewModel> Options { get; } = options;

    /// <summary>All / none / some of the available members; setting it applies to the available members.</summary>
    public bool? IsChecked
    {
        get => selection.GroupState(group);
        set
        {
            selection.SetGroup(group, value == true);
            changed();
        }
    }

    /// <summary>Inline error when a required group has nothing selected, otherwise null.</summary>
    public string? Error => selection.GroupError(group);

    public override bool HasError => Error is not null;

    public override string? CountText => $"{Options.Count(o => o.IsEnabled)}/{Options.Count}";

    /// <summary>Text for the group card: all, none or some enabled.</summary>
    public string SummaryText => IsChecked switch
    {
        true => "All enabled",
        false => "None enabled",
        _ => $"Some enabled ({Options.Count(o => o.IsEnabled)} of {Options.Count})",
    };

    /// <summary>Raises change notifications after any selection change.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasError));
    }
}
