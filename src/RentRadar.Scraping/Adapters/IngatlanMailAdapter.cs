using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Options;
using RentRadar.Contracts;
using RentRadar.Domain;

namespace RentRadar.Scraping.Adapters;

/// <summary>
/// ingatlan.com through its "Hirdetésfigyelő" e-mail alerts. The site's robots.txt disallows crawling result
/// pages, so the site itself mails new listings for saved searches; this adapter reads those e-mails over IMAP
/// from a Gmail label and parses the table inside. No ingatlan.com page is requested.
/// E-mail format (checked on real e-mails 14.11.2026), two rows per listing:
///   1) td: a[href=ingatlan.com/&lt;id&gt;] &gt; img (80x60) | td: a "Budapest, 10. kerület,(Felsőrákos), Gyógyszergyári utca"
///   2) td: &lt;b&gt;220 000&lt;/b&gt; Ft/hó | td: tégla | td: &lt;b&gt;30&lt;/b&gt; m² | td: &lt;b&gt;1+1&lt;/b&gt; fél
/// </summary>
public sealed partial class IngatlanMailAdapter(IOptions<MailOptions> options) : ISourceAdapter
{
    private const int DaysBack = 30;
    private static readonly string[] Roman =
        ["", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII", "XIII",
         "XIV", "XV", "XVI", "XVII", "XVIII", "XIX", "XX", "XXI", "XXII", "XXIII"];

    public string Name => "ingatlan";
    public string Title => "ingatlan.com (e-mail)";
    public IReadOnlyList<string> Countries => ["HU"];
    public string Homepage => "https://ingatlan.com";

    // the alerts are set up for Budapest only
    public bool Supports(string country, string city) =>
        country.Equals("HU", StringComparison.OrdinalIgnoreCase) && city.Trim().Equals("budapest", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^https?://(?:www\.)?ingatlan\.com/(\d{6,})\b")]
    private static partial Regex IdRe();

    [GeneratedRegex(@"Budapest,\s*(\d{1,2})\.\s*kerület", RegexOptions.IgnoreCase)]
    private static partial Regex DistrictNumRe();

    [GeneratedRegex(@"^\d+\.\s*kerület", RegexOptions.IgnoreCase)]
    private static partial Regex DistrictPart();

    [GeneratedRegex(@"^(\d+)(?:\s*\+\s*(\d+))?")]
    private static partial Regex RoomsRe();

    public async IAsyncEnumerable<RawListing> FetchAsync(ScrapeQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        var settings = MailSettings.Resolve(options.Value);
        if (!settings.IsConfigured)
            throw new AdapterException("mail is not configured: set Mail:User and Mail:AppPassword (or Mail:IniPath)");

        var messages = await FetchMessagesAsync(settings, ct);
        var latest = new Dictionary<string, RawListing>();
        // from old to new e-mails: a fresher price overwrites an older one
        foreach (var (sentAt, html) in messages.OrderBy(m => m.SentAt))
        foreach (var item in ParseEmailHtml(html, sentAt))
        {
            if (latest.TryGetValue(item.ExternalId, out var prev))
            {
                if (item.Images.Count == 0) item.Images = prev.Images;
                if (prev.PostedAt is not null) item.PostedAt = prev.PostedAt; // first time it appeared
            }
            latest[item.ExternalId] = item;
        }
        foreach (var item in latest.Values) yield return item;
    }

    private static async Task<List<(DateTime SentAt, string Html)>> FetchMessagesAsync(MailOptions s, CancellationToken ct)
    {
        var result = new List<(DateTime, string)>();
        using var client = new ImapClient { Timeout = 60_000 };
        await client.ConnectAsync("imap.gmail.com", 993, SecureSocketOptions.SslOnConnect, ct);
        try
        {
            await client.AuthenticateAsync(s.User, s.AppPassword, ct);
            IMailFolder folder;
            try { folder = await client.GetFolderAsync(s.Label, ct); }
            catch (FolderNotFoundException) { throw new AdapterException($"the mailbox has no label \"{s.Label}\""); }
            await folder.OpenAsync(FolderAccess.ReadOnly, ct);
            var since = DateTime.UtcNow.AddDays(-DaysBack);
            var uids = await folder.SearchAsync(SearchQuery.FromContains("ingatlan.com").And(SearchQuery.DeliveredAfter(since)), ct);
            foreach (var uid in uids)
            {
                var message = await folder.GetMessageAsync(uid, ct);
                if (message.HtmlBody is { } html) result.Add((message.Date.UtcDateTime, html));
            }
        }
        finally
        {
            await client.DisconnectAsync(true, CancellationToken.None);
        }
        return result;
    }

    private static string? ListingId(IElement a)
    {
        var m = IdRe().Match(a.GetAttribute("href") ?? "");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static IReadOnlyList<RawListing> ParseEmailHtml(string html, DateTime? sentAt = null)
    {
        var doc = Html.Parse(html);
        var result = new List<RawListing>();
        var seen = new HashSet<string>();
        foreach (var tr in doc.QuerySelectorAll("tr"))
        {
            var links = tr.QuerySelectorAll("a[href]").Where(a => ListingId(a) is not null).ToList();
            if (links.Count == 0 || tr.QuerySelector("tr") is not null) continue; // the listing header row, not a table wrapper
            var id = ListingId(links[0])!;
            if (seen.Contains(id)) continue;
            var dataRow = tr.NextElementSibling;
            while (dataRow is not null && dataRow.LocalName != "tr") dataRow = dataRow.NextElementSibling;
            if (dataRow is null || !Html.Text(dataRow).Contains("Ft")) continue;
            seen.Add(id);

            var img = tr.QuerySelectorAll("img").FirstOrDefault(i => Regex.IsMatch(i.GetAttribute("src") ?? "", "^https?://"));
            var addressLink = links.FirstOrDefault(a =>
            {
                var t = Html.Text(a);
                return t.Length > 0 && t != "térkép" && t != "Részletek" && a.QuerySelector("img") is null;
            });
            var rawAddress = addressLink is null ? "" : Html.Squash(Html.Text(addressLink));

            var cells = dataRow.QuerySelectorAll("td").ToList();
            IElement priceCell = cells.Count > 0 ? cells[0] : dataRow;
            var priceBold = priceCell.QuerySelector("b");
            var price = TextParsing.ParseNumber(priceBold is null ? Html.Text(priceCell) : Html.Text(priceBold));
            var priceText = Html.Text(priceCell);
            var currency = priceText.Contains('€') && !priceText.Contains("Ft") ? "EUR" : "HUF";
            var newBuild = priceText.Contains("új építésű");
            var building = cells.Count > 1 ? Html.Text(cells[1]) : "";
            var area = cells.Count > 2 ? TextParsing.ParseNumber(Html.Text(cells[2])) : null;
            double? rooms = null;
            if (cells.Count > 3)
            {
                var m = RoomsRe().Match(Html.Text(cells[3])); // "2", "1+1 fél"
                if (m.Success)
                    rooms = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) +
                            (m.Groups[2].Success ? 0.5 * int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0);
            }

            var (district, address) = SplitAddress(rawAddress);
            var description = string.Join(", ", new[] { building, newBuild ? "új építésű" : "" }.Where(x => x.Length > 0));
            result.Add(new RawListing
            {
                ExternalId = id,
                Url = $"https://ingatlan.com/{id}",
                Title = address ?? (rawAddress.Length > 0 ? rawAddress : $"ingatlan.com {id}"),
                Description = description.Length > 0 ? description : null,
                Price = price,
                Currency = currency,
                Rooms = rooms,
                AreaM2 = area,
                Address = address,
                District = district,
                Images = img is null ? [] : [img.GetAttribute("src")!],
                PostedAt = sentAt,
            });
        }
        return result;
    }

    /// <summary>
    /// "Budapest, 10. kerület, (Felsőrákos), Gyógyszergyári utca"
    /// → ("X. kerület", "Gyógyszergyári utca, Felsőrákos, X. kerület, Budapest").
    /// </summary>
    internal static (string? District, string? Address) SplitAddress(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return (null, null);
        string? district = null;
        var m = DistrictNumRe().Match(raw);
        if (m.Success && int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) is var n && n > 0 && n < Roman.Length)
            district = $"{Roman[n]}. kerület";
        var parts = raw.Split(',')
            .Select(p => p.Trim(' ', '(', ')'))
            .Where(p => p.Length > 0 && !p.Equals("budapest", StringComparison.OrdinalIgnoreCase) && !DistrictPart().IsMatch(p))
            .ToList();
        // parts: [neighbourhood, street] → street first, so de-duplication and geocoding work on the street
        var hood = parts.Count > 0 ? parts[0] : null;
        var street = parts.Count > 1 ? parts[^1] : null;
        return (district, string.Join(", ", new[] { street, hood, district, "Budapest" }.Where(x => !string.IsNullOrEmpty(x))));
    }
}
