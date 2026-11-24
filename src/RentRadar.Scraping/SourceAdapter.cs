using System.Net;
using System.Runtime.CompilerServices;
using RentRadar.Contracts;

namespace RentRadar.Scraping;

/// <summary>
/// A rental site. To add one: implement this interface (usually by deriving from <see cref="SourceAdapter"/>),
/// then register it in <see cref="ScrapingServiceCollectionExtensions.AddScraping"/>. Currency conversion,
/// geocoding, de-duplication, filters and the map work without further changes.
/// </summary>
public interface ISourceAdapter
{
    /// <summary>Machine name, unique; also the queue suffix (<c>scrape-{name}</c>).</summary>
    string Name { get; }
    string Title { get; }
    /// <summary>ISO 3166-1 alpha-2 codes; "*" means any country.</summary>
    IReadOnlyList<string> Countries { get; }
    string Homepage { get; }
    bool Supports(string country, string city);
    IAsyncEnumerable<RawListing> FetchAsync(ScrapeQuery query, CancellationToken ct);
}

public sealed class AdapterException(string message) : Exception(message);

/// <summary>Base class for adapters that read result pages over HTTP.</summary>
public abstract class SourceAdapter(IHttpClientFactory http) : ISourceAdapter
{
    public const string HttpClientName = "scraper";

    public abstract string Name { get; }
    public abstract string Title { get; }
    public abstract IReadOnlyList<string> Countries { get; }
    public virtual string Homepage => "";

    /// <summary>Pause between result pages, to be gentle with the site.</summary>
    protected virtual TimeSpan PageDelay => TimeSpan.FromSeconds(1.5);

    public virtual bool Supports(string country, string city) =>
        Countries.Contains("*") || Countries.Contains(country.ToUpperInvariant());

    public abstract IAsyncEnumerable<RawListing> FetchAsync(ScrapeQuery query, CancellationToken ct);

    protected async Task<(HttpStatusCode Status, string Body)> GetAsync(string url, CancellationToken ct)
    {
        using var response = await http.CreateClient(HttpClientName).GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return (response.StatusCode, body);
    }

    protected async Task<string> GetPageAsync(string url, string city, CancellationToken ct)
    {
        var (status, body) = await GetAsync(url, ct);
        if (status == HttpStatusCode.NotFound) throw new AdapterException($"{Title} does not know the city \"{city}\"");
        if ((int)status >= 400) throw new HttpRequestException($"{Title}: HTTP {(int)status} for {url}", null, status);
        return body;
    }

    protected Task DelayAsync(CancellationToken ct) => Task.Delay(PageDelay, ct);

    /// <summary>
    /// Shared paging loop for sites whose pages return a list of listings: stops when a page brings
    /// nothing new, when it is shorter than <paramref name="pageSize"/> or at <c>MaxPages</c>.
    /// </summary>
    protected async IAsyncEnumerable<RawListing> PagedAsync(
        ScrapeQuery query,
        Func<int, string> url,
        Func<string, IReadOnlyList<RawListing>> parse,
        int? pageSize,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var seen = new HashSet<string>();
        for (var page = 1; page <= query.MaxPages; page++)
        {
            if (page > 1) await DelayAsync(ct);
            var items = parse(await GetPageAsync(url(page), query.City, ct));
            var fresh = items.Where(i => seen.Add(i.ExternalId)).ToList();
            foreach (var item in fresh) yield return item;
            if (fresh.Count == 0 || (pageSize is { } size && items.Count < size)) yield break;
        }
    }
}
