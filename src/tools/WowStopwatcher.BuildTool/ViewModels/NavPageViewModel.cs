using CommunityToolkit.Mvvm.ComponentModel;

namespace WowStopwatcher.BuildTool.ViewModels;

/// <summary>A page shown as an item of the navigation pane.</summary>
public abstract class NavPageViewModel : ObservableObject
{
    public abstract string Title { get; }

    /// <summary>Small text next to the title, for example "2/3"; null for none.</summary>
    public virtual string? CountText => null;

    /// <summary>True when the page has a blocking problem; shows a badge in the pane.</summary>
    public virtual bool HasError => false;
}
