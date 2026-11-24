using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using RentRadar.Contracts;
using RentRadar.Domain;

namespace RentRadar.Scraping.Adapters;

/// <summary>
/// alberlet.hu (HU), plain HTML. Main parser: cards <c>div.advert[data-id]</c> (price, size, rooms, address, photos).
/// If the card markup changes, a fallback reads listing links (<c>/kiado-alberlet/..._&lt;id&gt;</c>) and their text.
/// Checked 14.11.2026 on Budapest.
/// </summary>
public sealed partial class AlberletAdapter(IHttpClientFactory http) : SourceAdapter(http)
{
    public const string Base = "https://www.alberlet.hu";

    public override string Name => "alberlet";
    public override string Title => "alberlet.hu";
    public override IReadOnlyList<string> Countries => ["HU"];
    public override string Homepage => Base;

    [GeneratedRegex(@"/kiado-alberlet/[^?#\s""']+_(\d+)/?$")]
    private static partial Regex ListingHref();

    [GeneratedRegex(@"([\d\s .]+)\s*(Ft|HUF|€|EUR)", RegexOptions.IgnoreCase)]
    private static partial Regex PriceRe();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*m\s*(?:2|²)", RegexOptions.IgnoreCase)]
    private static partial Regex AreaRe();

    [GeneratedRegex(@"(\d+)\s*szoba(?:\s*\+\s*(\d+)\s*félszoba)?", RegexOptions.IgnoreCase)]
    private static partial Regex RoomsRe();

    [GeneratedRegex(@"Budapest,?\s+([IVXL]+)\.\s*kerület", RegexOptions.IgnoreCase)]
    private static partial Regex DistrictRe();

    [GeneratedRegex(@"\s+(Budapest)\b")]
    private static partial Regex BeforeCity();

    // photos in the fallback layout: background-image: url("/hirdetes-kep/..._<id>-<n>@500x...")
    [GeneratedRegex(@"(/hirdetes-kep/[^""')\s&;]+?_(\d+)-\d+@[^""')\s&;]+)")]
    private static partial Regex ImageRe();

    public static string ListUrl(string city, int page)
    {
        var slug = TextParsing.Slugify(city);
        var location = slug == "budapest" ? "megye:budapest" : $"telepules:{slug}";
        var url = $"{Base}/kiado_alberlet/ingatlan-tipus:lakas/{location}/limit:48";
        return page == 1 ? url : $"{url}/page:{page}";
    }

    public override IAsyncEnumerable<RawListing> FetchAsync(ScrapeQuery query, CancellationToken ct) =>
        PagedAsync(query, p => ListUrl(query.City, p), ParseList, null, ct);

    public static IReadOnlyList<RawListing> ParseList(string html)
    {
        var doc = Html.Parse(html);
        var cards = doc.QuerySelectorAll("div.advert[data-id]");
        return cards.Length > 0
            ? cards.Select(ParseCard).OfType<RawListing>().ToList()
            : ParseLinks(doc, html);
    }

    private static string? ListingLink(IElement scope) =>
        scope.QuerySelectorAll("a[href]")
            .Select(a => a.GetAttribute("href")!)
            .FirstOrDefault(h => ListingHref().IsMatch(h.Split('?')[0]));

    private static (double? Rooms, double? Area) RoomsAndArea(string roomsText, string blob)
    {
        double? rooms = null;
        var rm = RoomsRe().Match(roomsText);
        if (rm.Success)
            rooms = double.Parse(rm.Groups[1].Value, CultureInfo.InvariantCulture) +
                    (rm.Groups[2].Success ? 0.5 * int.Parse(rm.Groups[2].Value, CultureInfo.InvariantCulture) : 0);
        var am = AreaRe().Match(blob);
        double? area = am.Success ? double.Parse(am.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture) : null;
        return (rooms, area);
    }

    /// <summary>"Aladár utca Budapest, I. kerület" → "Aladár utca, Budapest, I. kerület".</summary>
    private static string FixAddress(string address) => BeforeCity().Replace(address, ", $1", 1).TrimStart(',', ' ');

    private static string? District(string text)
    {
        var dm = DistrictRe().Match(text);
        return dm.Success ? $"{dm.Groups[1].Value.ToUpperInvariant()}. kerület" : null;
    }

    /// <summary>
    /// div.advert[data-id] &gt; div.image-container a.owl-lazy[data-src=/hirdetes-kep/...]
    ///                    &gt; div.advert-data: span.price, "27 m&lt;sup&gt;2&lt;/sup&gt;", div.rooms, div.address
    /// </summary>
    public static RawListing? ParseCard(IElement card)
    {
        var id = card.GetAttribute("data-id");
        var link = ListingLink(card);
        var priceEl = card.QuerySelector(".price");
        if (string.IsNullOrEmpty(id) || link is null || priceEl is null) return null;

        var data = card.QuerySelector(".advert-data") ?? card;
        var price = TextParsing.ParseNumber(Html.Text(priceEl));
        var priceBox = Html.Text(priceEl.ParentElement);
        var currency = priceBox.Contains('€') || priceBox.Contains("EUR", StringComparison.OrdinalIgnoreCase) ? "EUR" : "HUF";

        var blob = Html.Text(data);
        var roomsEl = card.QuerySelector(".rooms");
        var (rooms, area) = RoomsAndArea(roomsEl is null ? blob : Html.Text(roomsEl), blob);

        var addressEl = card.QuerySelector(".address");
        var address = addressEl is null ? null : FixAddress(Html.Text(addressEl));

        var images = card.QuerySelectorAll("[data-src]")
            .Select(e => e.GetAttribute("data-src") ?? "")
            .Where(src => src.Contains("/hirdetes-kep/"))
            .Select(src => Html.Absolute(Base, src))
            .Distinct()
            .Take(8)
            .ToList();

        return new RawListing
        {
            ExternalId = id,
            Url = Html.Absolute(Base, link.Split('?')[0]),
            Title = Truncate(address ?? blob, 300),
            Description = Truncate(blob, 2000),
            Price = price,
            Currency = currency,
            Rooms = rooms,
            AreaM2 = area,
            Address = address,
            District = District(address ?? blob),
            Images = images,
            Pets = TextParsing.DetectPets(blob),
        };
    }

    /// <summary>Fallback when the card markup changes: groups the text of all links to the same listing.</summary>
    public static IReadOnlyList<RawListing> ParseLinks(IDocument doc, string html)
    {
        var groups = new Dictionary<string, (string Url, List<string> Texts, List<string> Images)>();
        var order = new List<string>();
        foreach (var a in doc.QuerySelectorAll("a[href]"))
        {
            var href = a.GetAttribute("href")!.Split('?')[0];
            var m = ListingHref().Match(href);
            if (!m.Success) continue;
            var id = m.Groups[1].Value;
            if (!groups.TryGetValue(id, out var g))
            {
                g = (Html.Absolute(Base, href), new List<string>(), new List<string>());
                groups[id] = g;
                order.Add(id);
            }
            var text = Html.Text(a);
            if (text.Length > 0 && !g.Texts.Contains(text)) g.Texts.Add(text);
            foreach (var img in a.QuerySelectorAll("img"))
            {
                var src = img.GetAttribute("data-src") ?? img.GetAttribute("data-lazy") ?? img.GetAttribute("src");
                if (string.IsNullOrEmpty(src) || src.EndsWith(".svg") || src.StartsWith("data:")) continue;
                var full = Html.Absolute(Base, src);
                if (!g.Images.Contains(full)) g.Images.Add(full);
            }
        }

        foreach (Match m in ImageRe().Matches(html))
        {
            if (!groups.TryGetValue(m.Groups[2].Value, out var g)) continue;
            var full = Html.Absolute(Base, m.Groups[1].Value.Replace("&amp;", "&"));
            if (!g.Images.Contains(full)) g.Images.Add(full);
        }

        var result = new List<RawListing>();
        foreach (var id in order)
        {
            var (url, texts, images) = groups[id];
            var blob = string.Join(" | ", texts);
            var pm = PriceRe().Match(blob);
            if (!pm.Success) continue; // not a listing card, e.g. a "similar listings" link
            var cur = pm.Groups[2].Value.ToUpperInvariant();
            var (rooms, area) = RoomsAndArea(blob, blob);
            var address = texts.FirstOrDefault(t =>
                (t.Contains(',') || t.Contains("kerület", StringComparison.OrdinalIgnoreCase)) && !PriceRe().IsMatch(t));
            if (address is not null) address = FixAddress(address);
            var titleFromPrice = texts.First(t => PriceRe().IsMatch(t));
            result.Add(new RawListing
            {
                ExternalId = id,
                Url = url,
                Title = Truncate(address ?? titleFromPrice, 300),
                Description = Truncate(blob, 2000),
                Price = TextParsing.ParseNumber(pm.Groups[1].Value),
                Currency = cur is "FT" or "HUF" ? "HUF" : "EUR",
                Rooms = rooms,
                AreaM2 = area,
                Address = address,
                District = District(blob),
                Images = images.Take(8).ToList(),
                Pets = TextParsing.DetectPets(blob),
            });
        }
        return result;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
