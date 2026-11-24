using System.Text.Json.Nodes;
using RentRadar.Contracts;
using RentRadar.Domain;

namespace RentRadar.Scraping.Adapters;

/// <summary>
/// ingatlannet.hu (HU). Next.js: listings are JSON in <c>__NEXT_DATA__</c> (props.pageProps.serverEstates).
/// URL: <c>/kiado-lakas-&lt;city or district&gt;</c>, pages <c>?page=N</c>, 16 per page. No coordinates —
/// the point is found from the address during enrichment. Checked 10.11.2026.
/// </summary>
public sealed class IngatlannetAdapter(IHttpClientFactory http) : SourceAdapter(http)
{
    public const string Base = "https://www.ingatlannet.hu";
    private const int PageSize = 16;

    public override string Name => "ingatlannet";
    public override string Title => "Ingatlannet.hu";
    public override IReadOnlyList<string> Countries => ["HU"];
    public override string Homepage => Base;

    public static string ListUrl(string city, int page)
    {
        var url = $"{Base}/kiado-lakas-{TextParsing.Slugify(city)}";
        return page == 1 ? url : $"{url}?page={page}";
    }

    public override IAsyncEnumerable<RawListing> FetchAsync(ScrapeQuery query, CancellationToken ct) =>
        PagedAsync(query, p => ListUrl(query.City, p),
            html => ParsePage(html).Select(ParseEstate).OfType<RawListing>().ToList(), PageSize, ct);

    public static IReadOnlyList<JsonNode> ParsePage(string html) =>
        Html.NextData(html, "Ingatlannet").P("props", "pageProps", "serverEstates").Items().ToList();

    public static RawListing? ParseEstate(JsonNode e)
    {
        var id = e.P("id").Str();
        if (string.IsNullOrEmpty(id)) return null;
        var url = e.P("absolutUrl").Str() is { Length: > 0 } abs ? abs : Html.Absolute(Base, e.P("url").Str() ?? "");
        if (!url.Contains("/kiado-")) return null; // rentals only

        var images = new List<string>();
        if (e.P("mainPicture", "variants", "1", "url").Str() is { Length: > 0 } big) images.Add(big);

        var address = e.P("address").Str();
        string? district = null;
        if (address is not null && address.Contains("kerület")) district = address.Split(',')[^1].Trim();

        var area = e.P("buildingAreaSize").Truthy() ? e.P("buildingAreaSize").Num() : e.P("areaSize").Truthy() ? e.P("areaSize").Num() : null;
        return new RawListing
        {
            ExternalId = id,
            Url = url,
            Title = e.P("name").Str() ?? address ?? "",
            Price = e.P("price").Truthy() ? e.P("price").Num() : null,
            Currency = "HUF",
            Rooms = e.P("rooms").Truthy() ? e.P("rooms").Num() : null,
            AreaM2 = area,
            Address = address,
            District = district,
            Pets = e.P("pet").Bool(),
            Furnished = e.P("furnished").Bool(),
            Images = images,
            PostedAt = e.P("createdAt").Date(),
        };
    }
}
