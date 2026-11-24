using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using RentRadar.Contracts;
using RentRadar.Domain;

namespace RentRadar.Scraping.Adapters;

/// <summary>
/// olx.ro (RO), HTML result pages: the OLX API (<c>/api/</c>) is disallowed for robots in robots.txt.
/// Card (checked 14.11.2026): <c>[data-cy=l-card]</c> with an id, <c>[data-testid=ad-price]</c> "625 €",
/// <c>[data-testid=location-date]</c> "Bucuresti, Sectorul 6 - …", "56 m²". Rooms come from the title;
/// the page markup carries photos only for the first cards.
/// </summary>
public sealed partial class OlxRoAdapter(IHttpClientFactory http) : SourceAdapter(http)
{
    public const string Base = "https://www.olx.ro";

    public override string Name => "olx_ro";
    public override string Title => "OLX.ro";
    public override IReadOnlyList<string> Countries => ["RO"];
    public override string Homepage => Base;

    [GeneratedRegex(@"([\d][\d\s. ]*)\s*(€|EUR|lei|RON)", RegexOptions.IgnoreCase)]
    private static partial Regex PriceRe();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*m²")]
    private static partial Regex AreaRe();

    [GeneratedRegex(@"\b(\d)\s*(?:camere|cam\.?|camera)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RoomsRe();

    [GeneratedRegex(@"garsonier", RegexOptions.IgnoreCase)]
    private static partial Regex Studio();

    [GeneratedRegex(@";s=\d+x\d+")]
    private static partial Regex ImageSize();

    public static string ListUrl(string city, int page)
    {
        var url = $"{Base}/imobiliare/apartamente-garsoniere-de-inchiriat/{TextParsing.Slugify(city)}/";
        return page == 1 ? url : $"{url}?page={page}";
    }

    public override IAsyncEnumerable<RawListing> FetchAsync(ScrapeQuery query, CancellationToken ct) =>
        PagedAsync(query, p => ListUrl(query.City, p), ParseList, null, ct);

    public static IReadOnlyList<RawListing> ParseList(string html)
    {
        var result = new List<RawListing>();
        foreach (var card in Html.Parse(html).QuerySelectorAll("[data-cy=\"l-card\"]"))
        {
            var link = card.QuerySelector("a[href*=\"/d/oferta/\"]") ?? card.QuerySelector("a[href]");
            if (link is null) continue;
            var href = Html.Absolute(Base, link.GetAttribute("href")!.Split('?')[0].Split('#')[0]);
            var id = card.GetAttribute("id") is { Length: > 0 } cid ? cid : href.Split('-')[^1].Replace(".html", "");
            var titleEl = card.QuerySelector("[data-testid=\"ad-card-title\"] h4") ?? card.QuerySelector("h4, h6");
            var title = Html.Text(titleEl);

            double? price = null;
            string? currency = null;
            if (card.QuerySelector("[data-testid=\"ad-price\"]") is { } priceEl)
            {
                // the price element sometimes contains a <style> block — read text nodes only
                var pm = PriceRe().Match(Html.Text(priceEl, skipStyle: true));
                if (pm.Success)
                {
                    price = TextParsing.ParseNumber(pm.Groups[1].Value);
                    currency = pm.Groups[2].Value.ToUpperInvariant() is "€" or "EUR" ? "EUR" : "RON";
                }
            }

            string? location = null, district = null, address = null;
            if (card.QuerySelector("[data-testid=\"location-date\"]") is { } locEl)
            {
                var own = string.Join(' ', locEl.ChildNodes.OfType<IText>().Select(t => t.Data.Trim())).Trim();
                if (own.Length == 0) own = Html.Text(locEl);
                location = own.Split(" - ")[0].Trim(); // "Bucuresti, Sectorul 6"
                if (location.Length == 0) location = null;
            }
            if (location is not null)
            {
                var parts = location.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length > 1)
                {
                    district = location[(location.IndexOf(',') + 1)..].Trim();
                    address = string.Join(", ", Enumerable.Reverse(parts));
                }
                else address = location;
            }

            var blob = Html.Text(card);
            var am = AreaRe().Match(blob);
            double? rooms = null;
            var rm = RoomsRe().Match(title);
            if (rm.Success) rooms = double.Parse(rm.Groups[1].Value, CultureInfo.InvariantCulture);
            else if (Studio().IsMatch(title)) rooms = 1;

            var images = new List<string>();
            var src = card.QuerySelector("img")?.GetAttribute("src") ?? "";
            if (src.Contains("olxcdn.com")) images.Add(ImageSize().Replace(src, ";s=800x600"));

            result.Add(new RawListing
            {
                ExternalId = id,
                Url = href,
                Title = title,
                Price = price,
                Currency = currency,
                Rooms = rooms,
                AreaM2 = am.Success ? double.Parse(am.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture) : null,
                Address = address,
                District = district,
                Pets = TextParsing.DetectPets(title),
                Images = images,
            });
        }
        return result;
    }
}
