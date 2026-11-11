using WowStopwatcher.Core;
using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>
/// Gets rates from open.er-api.com (base USD) and keeps them in the settings so they survive a restart.
/// After a failed fetch it does not try again for five minutes.
/// </summary>
public sealed class ExchangeRateService : IExchangeRateService
{
    public const string CacheKey = "invoicing_fx_rates";

    private const string Url = "https://open.er-api.com/v6/latest/USD";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(5);
    private static readonly HttpClient Http = CreateClient();

    private readonly ISettingsRepository _settings;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ExchangeRates? _cached;
    private bool _cacheLoaded;
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;

    public ExchangeRateService(ISettingsRepository settings, TimeProvider time)
    {
        _settings = settings;
        _time = time;
    }

    public async Task<ExchangeRates?> GetRatesAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_cacheLoaded)
            {
                _cached = ExchangeRatesJson.TryParseCache(_settings.Get(CacheKey), out var stored) ? stored : null;
                _cacheLoaded = true;
            }

            var now = _time.GetUtcNow();
            if (_cached is { } cached && cached.IsFresh(now))
            {
                return cached;
            }

            if (now < _retryAt)
            {
                return _cached;
            }

            var fetched = await FetchAsync(now, cancellationToken);
            if (fetched is null)
            {
                _retryAt = now + RetryAfterFailure;
                return _cached;
            }

            _cached = fetched;
            _settings.Set(CacheKey, ExchangeRatesJson.Serialize(fetched));
            return fetched;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<ExchangeRates?> FetchAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);

            using var response = await Http.GetAsync(Url, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            return ExchangeRatesJson.TryParseApi(json, now, out var rates) ? rates : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("WowStopwatcher", "1.0"));
        return client;
    }
}
