using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>One preset color in the color picker of the company settings.</summary>
public sealed partial class InvoicingSwatchViewModel : ObservableObject
{
    private readonly Action<string> _pick;
    private readonly string _name;

    internal InvoicingSwatchViewModel(string name, string hex, bool isSelected, Action<string> pick)
    {
        _pick = pick;
        _name = Loc.T("settings.color." + name.ToLowerInvariant());
        Hex = hex;
        Brush = new SolidColorBrush(InvoicingColors.Parse(hex));
        _isSelected = isSelected;
    }

    public string Hex { get; }

    public IBrush Brush { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private bool _isSelected;

    public string Label => IsSelected ? Loc.T("settings.color.selected", ("name", _name)) : _name;

    public string Name => _name;

    [RelayCommand]
    private void Pick() => _pick(Hex);
}
