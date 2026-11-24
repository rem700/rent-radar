using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RentRadar.Api.Data;

namespace RentRadar.Api.Services;

public sealed class GeocodingOptions
{
    public const string Section = "Geocoding";
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org/";
    /// <summary>Nominatim's usage policy asks for an identifying User-Agent.</summary>
    public string UserAgent { get; set; } = "rent-radar/1.0 (+https://github.com/rem700/rent-radar)";
    public int MaxPerMessage { get; set; } = 40;
}

/// <summary>
/// Address → coordinates through Nominatim (OpenStreetMap): free, at most one request per second, results cached
/// in the database (failures included, network errors excluded).
/// </summary>
public sealed class GeocodingService(HttpClient http, RentRadarDb db, IOptions<GeocodingOptions> options, ILogger<GeocodingService> logger)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _last = DateTime.MinValue;
    private static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(1100);

    public async Task<(double Lat, double Lon)?> GeocodeAsync(string query, string? country, CancellationToken ct)
    {
        if (!options.Value.Enabled || string.IsNullOrWhiteSpace(query)) return null;
        var key = $"{query.Trim().ToLowerInvariant()}|{country?.ToLowerInvariant()}";
        if (key.Length > 500) key = key[..500];

        var cached = await db.GeoCache.AsNoTracking().FirstOrDefaultAsync(g => g.Query == key, ct);
        if (cached is not null) return cached.Lat is { } la && cached.Lon is { } lo ? (la, lo) : null;

        (double, double)? result;
        await Gate.WaitAsync(ct);
        try
        {
            var wait = Spacing - (DateTime.UtcNow - _last);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            var url = $"search?format=json&limit=1&q={Uri.EscapeDataString(query)}";
            if (!string.IsNullOrEmpty(country)) url += $"&countrycodes={country.ToLowerInvariant()}";
            var json = JsonNode.Parse(await http.GetStringAsync(url, ct)) as JsonArray;
            var first = json is { Count: > 0 } ? json[0] : null;
            result = first is null ? null : (Parse(first["lat"]), Parse(first["lon"]));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Geocoding failed for {Query}", query);
            return null; // network errors are not cached
        }
        finally
        {
            _last = DateTime.UtcNow;
            Gate.Release();
        }

        var entry = new GeoCacheEntry { Query = key, Lat = result?.Item1, Lon = result?.Item2 };
        db.GeoCache.Add(entry);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.Entry(entry).State = EntityState.Detached; } // cached concurrently by another flow
        return result;
    }

    private static double Parse(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<string>(out var s) ? double.Parse(s, CultureInfo.InvariantCulture) : n!.GetValue<double>();
}
