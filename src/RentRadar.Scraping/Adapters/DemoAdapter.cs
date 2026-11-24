using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using RentRadar.Contracts;

namespace RentRadar.Scraping.Adapters;

/// <summary>
/// Generates plausible listings for any city, to try the interface without touching real sites.
/// It is used only when a country has no real sources, or when picked explicitly.
/// </summary>
public sealed class DemoAdapter : ISourceAdapter
{
    private static readonly string[] Streets = ["Main St", "Park Ave", "River Rd", "Hill St", "Oak Ln", "Station Sq", "Market St", "Lake Blvd"];
    private static readonly Dictionary<string, (string Currency, double Rate)> Money = new()
    {
        ["HU"] = ("HUF", 395), ["RO"] = ("RON", 5), ["CZ"] = ("CZK", 25), ["PL"] = ("PLN", 4.3),
    };

    public string Name => "demo";
    public string Title => "Demo";
    public IReadOnlyList<string> Countries => ["*"];
    public string Homepage => "";
    public bool Supports(string country, string city) => true;

    public async IAsyncEnumerable<RawListing> FetchAsync(ScrapeQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(query.Country + query.City));
        var seed = BitConverter.ToInt32(hash, 0) & int.MaxValue;
        var rnd = new Random(seed);
        var lat0 = query.CityLat ?? 47.4979;
        var lon0 = query.CityLon ?? 19.0402;
        var (currency, rate) = Money.GetValueOrDefault(query.Country.ToUpperInvariant(), ("EUR", 1));
        var now = DateTime.UtcNow;
        double Uniform(double a, double b) => a + rnd.NextDouble() * (b - a);

        for (var i = 0; i < 30 * query.MaxPages; i++)
        {
            ct.ThrowIfCancellationRequested();
            var rooms = new[] { 1, 1, 2, 2, 2, 3, 3, 4 }[rnd.Next(8)];
            var area = rooms * rnd.Next(18, 31) + rnd.Next(5, 21);
            var eur = area * Uniform(9, 18);
            var street = Streets[rnd.Next(Streets.Length)];
            var pets = rnd.NextDouble() < 0.3;
            yield return new RawListing
            {
                ExternalId = $"{seed}-{i}",
                Url = $"https://example.com/{Uri.EscapeDataString(query.City.ToLowerInvariant())}/{seed}-{i}",
                Title = $"{rooms}-room flat, {area} m², {street} {rnd.Next(1, 121)}",
                Description = pets ? "Demo listing. Pets allowed." : "Demo listing.",
                Price = rate > 10 ? Math.Round(eur * rate / 100) * 100 : Math.Round(eur * rate),
                Currency = currency,
                Rooms = rooms,
                AreaM2 = area,
                Address = $"{street}, {query.City}",
                District = $"District {rnd.Next(1, 13)}",
                Lat = lat0 + Uniform(-0.05, 0.05),
                Lon = lon0 + Uniform(-0.07, 0.07),
                GeoPrecise = true,
                Pets = pets ? true : null,
                Furnished = rnd.NextDouble() < 0.6,
                Images = [$"demo/room-{rnd.Next(1, 7)}.svg"],
                PostedAt = now.AddHours(-rnd.Next(1, 24 * 20)),
            };
        }
    }
}
