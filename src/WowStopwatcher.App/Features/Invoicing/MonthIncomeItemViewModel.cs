using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>One company in the income card: its color dot and "1 950.00 CAD · Name".</summary>
public sealed partial class MonthIncomeItemViewModel : ObservableObject
{
    private readonly CompanyMonthIncome _income;

    internal MonthIncomeItemViewModel(CompanyMonthIncome income, bool amountsHidden)
    {
        _income = income;
        Brush = new SolidColorBrush(InvoicingColors.Parse(income.Color));
        _text = BuildText(amountsHidden);
    }

    public IBrush Brush { get; }

    [ObservableProperty]
    private string _text;

    internal void SetAmountsHidden(bool hidden) => Text = BuildText(hidden);

    private string BuildText(bool hidden) =>
        MoneyMask.Apply(InvoicingFormat.Money(_income.Amount, _income.Currency), hidden) + " · " + _income.Name;
}
