using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WowStopwatcher.App.ViewModels;

/// <summary>One button of the header navigation.</summary>
public sealed partial class NavItemViewModel(string key, string title, string? iconKey, ICommand command) : ObservableObject
{
    /// <summary>Key of the page this button opens.</summary>
    public string Key { get; } = key;

    public string Title { get; } = title;

    /// <summary>Resource key of the icon geometry, or null for a text-only button. The view resolves it.</summary>
    public string? IconKey { get; } = iconKey;

    public bool HasIcon => IconKey is not null;

    public ICommand Command { get; } = command;

    [ObservableProperty]
    private bool _isActive;
}
