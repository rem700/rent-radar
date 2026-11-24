using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using RentRadar.Domain;

namespace RentRadar.Api.Services;

public sealed class CurrencyOptions
{
    public const string Section = "Currency";
    /// <summary>false = always use the built-in approximate rates (tests, offline use).</summary>
    public bool Online { get; set; } = true;
    public string Url { get; set; } = "https://open.er-api.com/v6/latest/EUR";
}

public sealed record CurrencyRates(string Source, Dictionary<string, double> Rates);

/// <summary>
/// Exchange rates to EUR, so prices from different countries can be compared. Rates come from open.er-api.com
/// (free, no key) and are kept for 12 hours in HybridCache: in memory, plus Redis when it is configured, so all
/// API replicas share one download. If the service is unreachable, approximate built-in rates are used.
/// </summary>
public sealed class CurrencyService(HybridCache cache, IHttpClientFactory http, IOptions<CurrencyOptions> options, ILogger<CurrencyService> logger)
{
    private static readonly HybridCacheEntryOptions Entry = new()
    {
        Expiration = TimeSpan.FromHours(12),
        LocalCacheExpiration = TimeSpan.FromHours(1),
    };

    public ValueTask<CurrencyRates> GetRatesAsync(CancellationToken ct = default) =>
        cache.GetOrCreateAsync("fx:eur:v1", LoadAsync, Entry, cancellationToken: ct);

    private async ValueTask<CurrencyRates> LoadAsync(CancellationToken ct)
    {
        var fallback = new CurrencyRates("fallback", new Dictionary<string, double>(Currencies.Fallback));
        if (!options.Value.Online) return fallback;
        try
        {
            using var client = http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            var json = JsonNode.Parse(await client.GetStringAsync(options.Value.Url, ct));
            if (json?["result"]?.GetValue<string>() != "success" || json["rates"] is not JsonObject rates) return fallback;
            var merged = new Dictionary<string, double>(Currencies.Fallback);
            foreach (var (code, value) in rates)
                if (value is JsonValue v && v.TryGetValue<double>(out var rate) && rate > 0) merged[code] = rate;
            return new CurrencyRates("open.er-api.com", merged);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Exchange rates unavailable, using built-in approximate rates");
            return fallback;
        }
    }
}
