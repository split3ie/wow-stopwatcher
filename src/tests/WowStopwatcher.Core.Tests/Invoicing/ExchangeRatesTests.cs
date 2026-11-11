using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class ExchangeRatesTests
{
    private static readonly DateTimeOffset Fetched = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private static ExchangeRates Create() =>
        new([new("CAD", 1.25m), new("eur", 0.5m), new("BAD", 0m)], Fetched);

    [Fact]
    public void Converts_by_dividing_with_the_rate()
    {
        Assert.True(Create().TryToUsd(1950m, "CAD", out var usd));
        Assert.Equal(1560m, usd);
    }

    [Fact]
    public void Usd_is_converted_as_is()
    {
        Assert.True(Create().TryToUsd(1500m, "USD", out var usd));
        Assert.Equal(1500m, usd);
    }

    [Fact]
    public void Currency_code_ignores_case()
    {
        Assert.True(Create().TryToUsd(10m, "EUR", out var usd));
        Assert.Equal(20m, usd);
        Assert.True(Create().TryToUsd(10m, "cad", out _));
    }

    [Fact]
    public void Unknown_or_invalid_rate_is_not_converted()
    {
        Assert.False(Create().TryToUsd(10m, "XYZ", out var usd));
        Assert.Equal(0m, usd);
        Assert.False(Create().TryToUsd(10m, "BAD", out _));
    }

    [Fact]
    public void Fresh_for_one_hour()
    {
        var rates = Create();

        Assert.True(rates.IsFresh(Fetched));
        Assert.True(rates.IsFresh(Fetched.AddMinutes(59)));
        Assert.False(rates.IsFresh(Fetched.AddHours(1)));
        Assert.False(rates.IsFresh(Fetched.AddMinutes(-1)));
    }

    [Fact]
    public void Api_response_is_parsed()
    {
        const string json = """{"result":"success","time_last_update_unix":1,"rates":{"USD":1,"CAD":1.39}}""";

        Assert.True(ExchangeRatesJson.TryParseApi(json, Fetched, out var rates));
        Assert.Equal(1.39m, rates.Rates["CAD"]);
        Assert.Equal(Fetched, rates.FetchedUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"result":"error","error-type":"x"}""")]
    [InlineData("""{"result":"success"}""")]
    [InlineData("""{"result":"success","rates":{}}""")]
    public void Bad_api_response_is_rejected(string json)
    {
        Assert.False(ExchangeRatesJson.TryParseApi(json, Fetched, out _));
    }

    [Fact]
    public void Cache_text_round_trips()
    {
        var text = ExchangeRatesJson.Serialize(Create());

        Assert.True(ExchangeRatesJson.TryParseCache(text, out var rates));
        Assert.Equal(Fetched, rates.FetchedUtc);
        Assert.Equal(1.25m, rates.Rates["CAD"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("""{"fetched":"x","rates":{"CAD":1}}""")]
    [InlineData("""{"fetched":"2026-11-12T10:00:00+00:00"}""")]
    public void Damaged_cache_text_is_rejected(string? text)
    {
        Assert.False(ExchangeRatesJson.TryParseCache(text, out _));
    }
}
