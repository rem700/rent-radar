using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using RentRadar.Contracts;
using RentRadar.Domain;

namespace RentRadar.Scraping.Adapters;

/// <summary>
/// ingatlan.jofogas.hu (HU). The results page is Next.js: every listing is ready-made JSON in
/// <c>__NEXT_DATA__</c> (props.pageProps.adList.ads) with price, parameters and coordinates.
/// Rentals: <c>?st=u</c>, pages: <c>&amp;o=N</c>, 36 listings per page. Checked 09.11.2026.
/// </summary>
public sealed class JofogasAdapter(IHttpClientFactory http) : SourceAdapter(http)
{
    public const string Base = "https://ingatlan.jofogas.hu";
    private const string ImageBase = "https://img.jofogas.hu/bigthumbs/";

    public override string Name => "jofogas";
    public override string Title => "Jófogás Ingatlan";
    public override IReadOnlyList<string> Countries => ["HU"];
    public override string Homepage => Base;

    public static string ListUrl(string city, int page)
    {
        var url = $"{Base}/{TextParsing.Slugify(city)}/lakas?st=u";
        return page == 1 ? url : $"{url}&o={page}";
    }

    public override async IAsyncEnumerable<RawListing> FetchAsync(ScrapeQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        var lastPage = query.MaxPages;
        for (var page = 1; page <= lastPage; page++)
        {
            if (page > 1) await DelayAsync(ct);
            var (ads, pager) = ParsePage(await GetPageAsync(ListUrl(query.City, page), query.City, ct));
            foreach (var ad in ads)
                if (ParseAd(ad) is { } item) yield return item;
            if (pager.P("last_page").Num() is { } last) lastPage = Math.Min(lastPage, (int)last);
            if (ads.Count == 0) yield break;
        }
    }

    public static (IReadOnlyList<JsonNode> Ads, JsonNode? Pager) ParsePage(string html)
    {
        var adList = Html.NextData(html, "Jófogás").P("props", "pageProps", "adList");
        return (adList.P("ads").Items().ToList(), adList.P("pager"));
    }

    public static RawListing? ParseAd(JsonNode ad)
    {
        var id = ad.P("list_id").Str();
        var url = ad.P("url").Str();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(url)) return null;
        var type = ad.P("type", "value").Str();
        if (type is not null && type != "u") return null; // rentals only

        var parameters = new Dictionary<string, List<string>>();
        foreach (var p in ad.P("parameters").Items())
        {
            var key = p.P("key").Str();
            if (key is null) continue;
            parameters[key] = p.P("values").Items()
                .Select(v => (v.P("label").Truthy() ? v.P("label").Str() : v.P("value").Str() ?? "")!.Trim())
                .ToList();
        }
        string? First(string key) => parameters.TryGetValue(key, out var v) && v.Count > 0 ? v[0] : null;

        var rooms = TextParsing.ParseNumber(First("rooms"));
        var half = TextParsing.ParseNumber(First("half_room"));
        if (rooms is not null && half is > 0) rooms += 0.5 * half;

        bool? pets = null;
        var animal = (First("animal_friendly") ?? "").ToLowerInvariant();
        if (animal.Length > 0) pets = animal.StartsWith("igen");
        pets ??= TextParsing.DetectPets(ad.P("subject").Str(), ad.P("body").Str());

        bool? furnished = null;
        var furniture = (First("has_furniture") ?? "").ToLowerInvariant();
        if (furniture.Length > 0) furnished = furniture.StartsWith("igen") || furniture.Contains("részben");

        var district = First("city"); // "XIV. kerület" for Budapest
        var area = First("jfg_area");
        var street = string.Join(' ', new[] { First("jfg_street"), First("jfg_street_number") }.Where(x => !string.IsNullOrEmpty(x)));
        var city = ad.P("region", "label").Str() ?? "";
        var address = string.Join(", ", new[] { street, area, district, city != district ? city : null }
            .Where(x => !string.IsNullOrEmpty(x)));

        DateTime? posted = null;
        var ts = ad.P("list_time", "value").Str();
        if (long.TryParse(ts, out var unix)) posted = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;

        double? lat = null, lon = null;
        if (ad.P("latitude").Truthy() && ad.P("longitude").Truthy())
        {
            lat = ad.P("latitude").Num();
            lon = ad.P("longitude").Num();
            if (lat is null || lon is null) lat = lon = null;
        }

        var images = ad.P("images").Items()
            .Select(i => i.P("name").Str())
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => ImageBase + n)
            .Take(8)
            .ToList();
        if (images.Count == 0 && ad.P("logo_url").Str() is { Length: > 0 } logo) images.Add(logo);

        var description = ad.P("body").Str() ?? "";
        var extra = parameters
            .Where(kv => kv.Key is "common_cost" or "deposit" or "availability")
            .Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value)}")
            .ToList();
        if (extra.Count > 0) description = (description + "\n\n" + string.Join('\n', extra)).Trim();

        return new RawListing
        {
            ExternalId = id,
            Url = url,
            Title = ad.P("subject").Str() ?? "",
            Description = description.Length > 0 ? description : null,
            Price = ad.P("price", "value").Num(),
            Currency = "HUF",
            Rooms = rooms,
            AreaM2 = TextParsing.ParseNumber(First("size")),
            Floor = First("floor"),
            Address = address.Length > 0 ? address : null,
            District = district,
            Lat = lat,
            Lon = lon,
            // Jófogás gives a point for the address; without a street it is the district centre
            GeoPrecise = lat is not null && street.Length > 0,
            Pets = pets,
            Furnished = furnished,
            Images = images,
            PostedAt = posted,
        };
    }
}
