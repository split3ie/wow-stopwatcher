using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>
/// The "This month" card: income of the current month across all companies in US dollars, with the amount of each company in its own currency.
/// The breakdown is shown at once; rates load in the background and the total follows.
/// </summary>
public sealed partial class MonthIncomeViewModel : ObservableObject
{
    private readonly InvoicingService _service;
    private readonly IExchangeRateService _rates;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private IReadOnlyList<CompanyMonthIncome> _income = [];
    private ExchangeRates? _loadedRates;
    private bool _ratesLoaded;
    private bool _amountsHidden;
    private int _version;
    private CancellationTokenSource? _cancel;

    internal MonthIncomeViewModel(
        InvoicingService service,
        IExchangeRateService rates,
        IUiDispatcher dispatcher,
        TimeProvider time,
        bool amountsHidden)
    {
        _service = service;
        _rates = rates;
        _dispatcher = dispatcher;
        _time = time;
        _amountsHidden = amountsHidden;
    }

    public ObservableCollection<MonthIncomeItemViewModel> Items { get; } = [];

    public bool HasIncome => Items.Count > 0;

    public bool HasNoIncome => Items.Count == 0;

    [ObservableProperty]
    private string _totalText = string.Empty;

    [ObservableProperty]
    private bool _hasTotal;

    /// <summary>The rates credit, or "Exchange rates unavailable"; empty while rates load.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHint))]
    private string _hintText = string.Empty;

    public bool HasHint => HintText.Length > 0;

    /// <summary>Names the currencies that have no rate and are left out of the total; empty when there are none.</summary>
    [ObservableProperty]
    private string _noRateText = string.Empty;

    [ObservableProperty]
    private bool _hasNoRate;

    /// <summary>Reads the income again and, when there is any, the rates (cached for an hour).</summary>
    public void Refresh()
    {
        var version = ++_version;
        _cancel?.Cancel();
        _cancel?.Dispose();
        _cancel = null;

        _income = _service.GetMonthIncome();
        Items.Clear();
        foreach (var income in _income)
        {
            Items.Add(new MonthIncomeItemViewModel(income, _amountsHidden));
        }

        OnPropertyChanged(nameof(HasIncome));
        OnPropertyChanged(nameof(HasNoIncome));
        _ratesLoaded = false;
        _loadedRates = null;
        UpdateTotal();

        if (_income.Count > 0)
        {
            _cancel = new CancellationTokenSource();
            _ = LoadRatesAsync(version, _cancel.Token);
        }
    }

    public void SetAmountsHidden(bool hidden)
    {
        _amountsHidden = hidden;
        foreach (var item in Items)
        {
            item.SetAmountsHidden(hidden);
        }

        UpdateTotal();
    }

    /// <summary>Stops a rates request that is still running.</summary>
    public void Cancel()
    {
        _version++;
        _cancel?.Cancel();
    }

    private async Task LoadRatesAsync(int version, CancellationToken cancellationToken)
    {
        ExchangeRates? rates = null;
        try
        {
            rates = await Task.Run(() => _rates.GetRatesAsync(cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _dispatcher.Post(() =>
        {
            if (version != _version)
            {
                return;
            }

            _loadedRates = rates;
            _ratesLoaded = true;
            UpdateTotal();
        });
    }

    private void UpdateTotal()
    {
        HasTotal = false;
        TotalText = string.Empty;
        HintText = string.Empty;
        NoRateText = string.Empty;
        HasNoRate = false;
        if (!_ratesLoaded || _income.Count == 0)
        {
            return;
        }

        if (_loadedRates is not { } rates)
        {
            HintText = Loc.T("invoicing.income.unavailable");
            return;
        }

        decimal total = 0;
        var converted = 0;
        var missing = new List<string>();
        foreach (var income in _income)
        {
            if (rates.TryToUsd(income.Amount, income.Currency, out var usd))
            {
                total += usd;
                converted++;
            }
            else if (!missing.Contains(income.Currency))
            {
                missing.Add(income.Currency);
            }
        }

        if (converted > 0)
        {
            var money = MoneyMask.Apply(InvoicingFormat.Money(total, "USD"), _amountsHidden);
            TotalText = Loc.T("invoicing.income.total", ("amount", money));
            HasTotal = true;
        }

        if (missing.Count > 0)
        {
            NoRateText = Loc.T("invoicing.income.noRate", ("currencies", string.Join(", ", missing)));
            HasNoRate = true;
        }

        var updated = TimeZoneInfo.ConvertTime(rates.FetchedUtc, _time.LocalTimeZone);
        HintText = Loc.T("invoicing.income.rates", ("time", updated.ToString("t", Loc.Culture)));
    }
}
