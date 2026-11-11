using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>One company tab. The selected tab is tinted with the company color.</summary>
public sealed partial class CompanyTabViewModel : ObservableObject
{
    private readonly Action<string> _select;

    internal CompanyTabViewModel(Company company, bool isSelected, Action<string> select)
    {
        _select = select;
        Id = company.Id;
        _isSelected = isSelected;
        Update(company);
    }

    public string Id { get; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private IBrush _brush = Brushes.Transparent;

    [ObservableProperty]
    private IBrush _tintBrush = Brushes.Transparent;

    [ObservableProperty]
    private IBrush _inkBrush = Brushes.Transparent;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Takes the name and color of the company.</summary>
    internal void Update(Company company)
    {
        var color = InvoicingColors.Parse(company.Color);
        Name = company.Name;
        Brush = new SolidColorBrush(color);
        TintBrush = new SolidColorBrush(InvoicingColors.Tint(color));
        InkBrush = new SolidColorBrush(InvoicingColors.Ink(color));
    }

    [RelayCommand]
    private void Select() => _select(Id);
}
