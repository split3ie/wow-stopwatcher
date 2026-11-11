namespace WowStopwatcher.Core.Invoicing;

/// <summary>US dollar exchange rates: how many units of each currency one US dollar buys, and when they were fetched.</summary>
public sealed class ExchangeRates
{
    /// <summary>How long fetched rates are used before they are fetched again.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromHours(1);

    private readonly Dictionary<string, decimal> _rates;

    /// <summary>Takes the rates per currency code (case-insensitive); entries that are not positive are ignored. USD is always 1.</summary>
    public ExchangeRates(IEnumerable<KeyValuePair<string, decimal>> rates, DateTimeOffset fetchedUtc)
    {
        _rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, rate) in rates)
        {
            if (rate > 0)
            {
                _rates[code.Trim()] = rate;
            }
        }

        _rates["USD"] = 1m;
        FetchedUtc = fetchedUtc;
    }

    /// <summary>When the rates were fetched.</summary>
    public DateTimeOffset FetchedUtc { get; }

    /// <summary>The rate of each currency against one US dollar.</summary>
    public IReadOnlyDictionary<string, decimal> Rates => _rates;

    /// <summary>True while the rates are younger than <see cref="FreshFor"/> at <paramref name="now"/>.</summary>
    public bool IsFresh(DateTimeOffset now) => now >= FetchedUtc && now - FetchedUtc < FreshFor;

    /// <summary>Converts <paramref name="amount"/> of <paramref name="currency"/> (ISO code, any case) to US dollars; false for a currency without a rate.</summary>
    public bool TryToUsd(decimal amount, string currency, out decimal usd)
    {
        if (_rates.TryGetValue(currency.Trim(), out var rate))
        {
            usd = amount / rate;
            return true;
        }

        usd = 0;
        return false;
    }
}
