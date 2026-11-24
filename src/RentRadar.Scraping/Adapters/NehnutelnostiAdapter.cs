using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using RentRadar.Contracts;
using RentRadar.Domain;

namespace RentRadar.Scraping.Adapters;

/// <summary>
/// nehnutelnosti.sk (SK). The results page <c>/vysledky/byty/&lt;city&gt;/prenajom</c> (<c>?page=N</c>, 30 per page)
/// carries schema.org JSON-LD: SearchResultsPage → ItemList → Product/Offer with price, size, category
/// ("2 izbový byt"), photos and date. The address ("Košická, Bratislava-Ružinov, okres Bratislava II")
/// comes from the card that links to <c>/detail/&lt;id&gt;/</c>. Checked 14.11.2026.
/// </summary>
public sealed partial class NehnutelnostiAdapter(IHttpClientFactory http) : SourceAdapter(http)
{
    public const string Base = "https://www.nehnutelnosti.sk";
    private const int PageSize = 30;

    public override string Name => "nehnutelnosti";
    public override string Title => "Nehnutelnosti.sk";
    public override IReadOnlyList<string> Countries => ["SK"];
    public override string Homepage => Base;

    [GeneratedRegex(@"(\d)\s*[- ]?\s*izb", RegexOptions.IgnoreCase)]
    private static partial Regex RoomsRe();

    [GeneratedRegex(@"gars[oó]nk", RegexOptions.IgnoreCase)]
    private static partial Regex Studio();

    [GeneratedRegex(@"[Pp]odlažie:\s*(\d+)")]
    private static partial Regex FloorRe();

    public static string ListUrl(string city, int page)
    {
        var url = $"{Base}/vysledky/byty/{TextParsing.Slugify(city)}/prenajom";
        return page == 1 ? url : $"{url}?page={page}";
    }

    public override IAsyncEnumerable<RawListing> FetchAsync(ScrapeQuery query, CancellationToken ct) =>
        PagedAsync(query, p => ListUrl(query.City, p), ParsePage, PageSize, ct);

    public static IReadOnlyList<RawListing> ParsePage(string html)
    {
        var doc = Html.Parse(html);
        var items = new List<JsonNode>();
        foreach (var script in doc.QuerySelectorAll("script[type=\"application/ld+json\"]"))
        {
            JsonNode? data;
            try { data = JsonNode.Parse(script.TextContent); }
            catch (JsonException) { continue; }
            if (data is not JsonObject) continue;
            IEnumerable<JsonNode> nodes = data.P("@graph") is JsonArray graph ? graph.Items() : [data];
            foreach (var node in nodes)
            {
                if (node.P("@type").Str() != "SearchResultsPage") continue;
                foreach (var li in node.P("mainEntity", "itemListElement").Items())
                    if (li.P("item") is JsonObject item) items.Add(item);
            }
        }
        if (items.Count == 0 && !html.Contains("SearchResultsPage"))
            throw new AdapterException("Nehnutelnosti.sk: no search results data on the page, the site layout has changed");
        return items.Select(i => ParseItem(i, doc)).OfType<RawListing>().ToList();
    }

    /// <summary>Text pieces of the card that links to this listing (and to no other).</summary>
    private static IReadOnlyList<string> CardTexts(IDocument doc, string id)
    {
        IElement? card = doc.QuerySelector($"a[href*=\"/detail/{id}/\"]");
        if (card is null) return [];
        for (var i = 0; i < 10 && card.ParentElement is not null; i++)
        {
            card = card.ParentElement;
            var other = card.QuerySelectorAll("a[href*=\"/detail/\"]")
                .Any(a => !(a.GetAttribute("href") ?? "").Contains($"/detail/{id}/"));
            if (other) return [];
            var text = Html.Text(card);
            if (text.Contains('€') && text.Contains("okres")) break;
        }
        return card.GetDescendants().OfType<IText>().Select(t => t.Data.Trim()).Where(t => t.Length > 0).ToList();
    }

    public static RawListing? ParseItem(JsonNode it, IDocument? doc = null)
    {
        var url = it.P("url").Str();
        var id = (it.P("@id").Str() ?? "").Split('/')[^1];
        if (id.Length == 0 && url is not null) id = url.Split("/detail/")[^1].Split('/')[0];
        if (string.IsNullOrEmpty(url) || id.Length == 0) return null;

        var spec = it.P("priceSpecification");
        var price = spec.P("price").Num() ?? TextParsing.ParseNumber(it.P("offers", "price").Str());
        var currency = spec.P("priceCurrency").Str() ?? it.P("offers", "priceCurrency").Str() ?? "EUR";
        var category = it.P("category").Str() ?? "";
        var name = it.P("name").Str() ?? "";

        var rooms = it.P("numberOfRooms").Num();
        if (rooms is not > 0)
        {
            var m = RoomsRe().Match(category);
            if (!m.Success) m = RoomsRe().Match(name);
            if (m.Success) rooms = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            else if (Studio().IsMatch(category + " " + name)) rooms = 1;
            else rooms = null;
        }

        var description = it.P("description").Str() ?? "";
        var fm = FloorRe().Match(description);

        string? address = null, district = null;
        if (doc is not null)
        {
            var piece = CardTexts(doc, id).FirstOrDefault(t => t.Contains("okres") && t.Contains(','));
            if (piece is not null)
            {
                address = piece.Split(", okres")[0].Trim(); // "Košická, Bratislava-Ružinov"
                district = address.Split(',', StringSplitOptions.TrimEntries)[^1];
            }
        }

        List<string> images = it.P("image") switch
        {
            JsonArray arr => arr.Items().Select(x => x.Str()).OfType<string>().ToList(),
            JsonValue v when v.Str() is { } single => [single],
            _ => new List<string>(),
        };

        bool? furnished = null;
        var low = description.ToLowerInvariant();
        if (low.Contains("nezariaden")) furnished = false;
        else if (low.Contains("zariaden")) furnished = true;

        return new RawListing
        {
            ExternalId = id,
            Url = url,
            Title = name.Length > 0 ? name : category,
            Description = description.Length == 0 ? null : description.Length > 2000 ? description[..2000] : description,
            Price = price,
            Currency = currency,
            Rooms = rooms,
            AreaM2 = it.P("floorSize", "value").Num(),
            Floor = fm.Success ? fm.Groups[1].Value : null,
            Address = address,
            District = district,
            Pets = TextParsing.DetectPets(name, description),
            Furnished = furnished,
            Images = images.Take(8).ToList(),
            PostedAt = it.P("validFrom").Date(),
        };
    }
}
