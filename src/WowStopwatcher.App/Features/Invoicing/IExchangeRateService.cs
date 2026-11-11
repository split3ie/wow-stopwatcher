using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>US dollar exchange rates for the income card.</summary>
public interface IExchangeRateService
{
    /// <summary>
    /// The rates, fetched at most once an hour. When fetching fails the last known rates (possibly stale) are returned, or null when there are none.
    /// </summary>
    Task<ExchangeRates?> GetRatesAsync(CancellationToken cancellationToken);
}
