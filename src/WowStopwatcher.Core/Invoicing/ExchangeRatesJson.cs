using System.Text.Json;

namespace WowStopwatcher.Core.Invoicing;

/// <summary>Reads the open.er-api.com response and the copy of the rates kept in the settings.</summary>
public static class ExchangeRatesJson
{
    /// <summary>Reads <c>{"result":"success","rates":{"USD":1,"CAD":1.39}}</c>; false for a failed result, missing rates or bad JSON.</summary>
    public static bool TryParseApi(string json, DateTimeOffset fetchedUtc, out ExchangeRates rates)
    {
        rates = null!;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("result", out var result) || result.GetString() != "success"
                || !root.TryGetProperty("rates", out var list) || list.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            rates = new ExchangeRates(ReadRates(list), fetchedUtc);
            return rates.Rates.Count > 1;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>The text kept in the settings: <c>{"fetched":"...","rates":{...}}</c>.</summary>
    public static string Serialize(ExchangeRates rates) =>
        JsonSerializer.Serialize(new { fetched = rates.FetchedUtc, rates = rates.Rates });

    /// <summary>Reads text made by <see cref="Serialize"/>; false for missing or damaged text.</summary>
    public static bool TryParseCache(string? json, out ExchangeRates rates)
    {
        rates = null!;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("fetched", out var fetched) || !fetched.TryGetDateTimeOffset(out var at)
                || !root.TryGetProperty("rates", out var list) || list.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            rates = new ExchangeRates(ReadRates(list), at);
            return rates.Rates.Count > 1;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static List<KeyValuePair<string, decimal>> ReadRates(JsonElement list)
    {
        var result = new List<KeyValuePair<string, decimal>>();
        foreach (var property in list.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDecimal(out var rate))
            {
                result.Add(new(property.Name, rate));
            }
        }

        return result;
    }
}
