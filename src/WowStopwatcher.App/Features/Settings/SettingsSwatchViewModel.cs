using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WowStopwatcher.App.Features.Settings;

/// <summary>One color choice in a stopwatch's palette on the Settings page.</summary>
public sealed partial class SettingsSwatchViewModel : ObservableObject
{
    /// <summary>The stopwatch color palette: name and hex value.</summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> Palette =
    [
        ("Blue", "#2F6FEB"),
        ("Green", "#1F9D55"),
        ("Orange", "#C96A0C"),
        ("Purple", "#7C4DDB"),
        ("Pink", "#D6407A"),
        ("Teal", "#0E8C8C"),
        ("Red", "#D92D20"),
        ("Amber", "#B8860B"),
        ("Lime", "#5C9E1B"),
        ("Cyan", "#0891B2"),
        ("Indigo", "#4F46E5"),
        ("Slate", "#475569"),
        ("Brown", "#8B5A2B"),
        ("Magenta", "#B5179E"),
        ("Olive", "#6B7A1F"),
        ("Navy", "#1E3A8A"),
    ];

    private readonly SettingsStopwatchViewModel _owner;
    private readonly string _name;

    public SettingsSwatchViewModel(SettingsStopwatchViewModel owner, string name, string hex, bool isSelected)
    {
        _owner = owner;
        _name = name;
        Hex = hex;
        Brush = new SolidColorBrush(Avalonia.Media.Color.Parse(hex));
        _isSelected = isSelected;
    }

    public string Hex { get; }

    public IBrush Brush { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private bool _isSelected;

    public string Label => IsSelected ? _name + ", selected" : _name;

    [RelayCommand]
    private void Pick() => _owner.SelectColor(Hex);
}
