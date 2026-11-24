using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using RentRadar.Contracts;
using RentRadar.Domain;

namespace RentRadar.Scraping.Adapters;

/// <summary>
/// storia.ro (RO). Next.js: listings are JSON in <c>__NEXT_DATA__</c> (props.pageProps.data.searchAds.items).
/// URL: <c>/ro/rezultate/inchiriere/apartament/&lt;county&gt;/&lt;city&gt;</c>, pages <c>?page=N</c>, 36 per page.
/// Bucharest is simply <c>/bucuresti</c>. Checked 14.11.2026.
/// </summary>
public sealed class StoriaAdapter(IHttpClientFactory http) : SourceAdapter(http)
{
    public const string Base = "https://www.storia.ro";

    /// <summary>City → county/city path; other cities are tried as "&lt;slug&gt;/&lt;slug&gt;", then "&lt;slug&gt;".</summary>
    private static readonly Dictionary<string, string> CityPaths = new()
    {
        ["bucuresti"] = "bucuresti", ["bucharest"] = "bucuresti", ["bukarest"] = "bucuresti",
        ["cluj-napoca"] = "cluj/cluj--napoca", ["cluj"] = "cluj/cluj--napoca",
        ["timisoara"] = "timis/timisoara", ["iasi"] = "iasi/iasi", ["constanta"] = "constanta/constanta",
        ["brasov"] = "brasov/brasov", ["sibiu"] = "sibiu/sibiu", ["oradea"] = "bihor/oradea",
        ["craiova"] = "dolj/craiova", ["ploiesti"] = "prahova/ploiesti",
    };

    private static readonly Dictionary<string, double> Rooms = new()
    {
        ["ONE"] = 1, ["TWO"] = 2, ["THREE"] = 3, ["FOUR"] = 4, ["FIVE"] = 5, ["SIX"] = 6, ["SEVEN"] = 7,
        ["EIGHT"] = 8, ["NINE"] = 9, ["TEN"] = 10, ["MORE"] = 10,
    };

    private static readonly Dictionary<string, string> Floors = new()
    {
        ["CELLAR"] = "-1", ["GROUND"] = "0", ["FIRST"] = "1", ["SECOND"] = "2", ["THIRD"] = "3", ["FOURTH"] = "4",
        ["FIFTH"] = "5", ["SIXTH"] = "6", ["SEVENTH"] = "7", ["EIGHTH"] = "8", ["NINTH"] = "9", ["TENTH"] = "10",
        ["ABOVE_TENTH"] = "10+", ["GARRET"] = "mansardă",
    };

    public override string Name => "storia";
    public override string Title => "Storia.ro";
    public override IReadOnlyList<string> Countries => ["RO"];
    public override string Homepage => Base;

    public static IReadOnlyList<string> CityPathsFor(string city)
    {
        var slug = TextParsing.Slugify(city);
        return CityPaths.TryGetValue(slug, out var path) ? [path] : [$"{slug}/{slug}", slug];
    }

    public static string ListUrl(string path, int page)
    {
        var url = $"{Base}/ro/rezultate/inchiriere/apartament/{path}";
        return page == 1 ? url : $"{url}?page={page}";
    }

    public override async IAsyncEnumerable<RawListing> FetchAsync(ScrapeQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        string? path = null, html = null;
        foreach (var candidate in CityPathsFor(query.City))
        {
            var (status, body) = await GetAsync(ListUrl(candidate, 1), ct);
            if (status == HttpStatusCode.OK && body.Contains("__NEXT_DATA__"))
            {
                (path, html) = (candidate, body);
                break;
            }
        }
        if (path is null || html is null) throw new AdapterException($"Storia does not know the city \"{query.City}\"");

        for (var page = 1; page <= query.MaxPages; page++)
        {
            if (page > 1)
            {
                await DelayAsync(ct);
                html = await GetPageAsync(ListUrl(path, page), query.City, ct);
            }
            var (items, pagination) = ParsePage(html);
            foreach (var it in items)
                if (ParseItem(it) is { } item) yield return item;
            var totalPages = pagination.P("totalPages").Num() ?? page;
            if (items.Count == 0 || page >= totalPages) yield break;
        }
    }

    public static (IReadOnlyList<JsonNode> Items, JsonNode? Pagination) ParsePage(string html)
    {
        var ads = Html.NextData(html, "Storia").P("props", "pageProps", "data", "searchAds");
        return (ads.P("items").Items().ToList(), ads.P("pagination"));
    }

    public static RawListing? ParseItem(JsonNode a)
    {
        var id = a.P("id").Str();
        var slug = a.P("slug").Str();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(slug)) return null;
        if (a.P("transaction").Str() is { } tx && tx != "RENT") return null;

        double? price = null;
        string? currency = null;
        var tp = a.P("rentPrice").Truthy() ? a.P("rentPrice") : a.P("totalPrice");
        if (tp.Truthy() && a.P("hidePrice").Bool() != true)
        {
            price = tp.P("value").Num();
            currency = tp.P("currency").Str();
        }

        var location = a.P("location");
        var street = location.P("address", "street");
        var streetText = string.Join(' ', new[] { street.P("name").Str(), street.P("number").Str() }.Where(x => !string.IsNullOrEmpty(x)));
        var levels = new Dictionary<string, string>();
        foreach (var l in location.P("reverseGeocoding", "locations").Items())
            if (l.P("locationLevel").Str() is { } level && l.P("name").Str() is { } name)
                levels[level] = name;
        string? Level(params string[] keys) => keys.Select(k => levels.GetValueOrDefault(k)).FirstOrDefault(v => v is not null);
        var district = Level("sector", "district", "residential");
        var city = Level("city", "county");
        var address = string.Join(", ", new[] { streetText, district, city }.Where(x => !string.IsNullOrEmpty(x)));

        var images = a.P("images").Items().Take(8)
            .Select(im => im.P("large").Str() ?? im.P("medium").Str() ?? im.P("small").Str())
            .OfType<string>()
            .ToList();

        var description = a.P("shortDescription").Str();
        return new RawListing
        {
            ExternalId = id,
            Url = $"{Base}/ro/oferta/{slug}",
            Title = a.P("title").Str() ?? address,
            Description = description,
            Price = price,
            Currency = currency,
            Rooms = Rooms.TryGetValue(a.P("roomsNumber").Str() ?? "", out var r) ? r : null,
            AreaM2 = a.P("areaInSquareMeters").Num(),
            Floor = Floors.GetValueOrDefault(a.P("floorNumber").Str() ?? ""),
            Address = address.Length > 0 ? address : null,
            District = district,
            Pets = TextParsing.DetectPets(a.P("title").Str(), description),
            Images = images,
            PostedAt = a.P("dateCreated").Date(),
        };
    }
}
