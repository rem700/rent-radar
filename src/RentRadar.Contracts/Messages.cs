namespace RentRadar.Contracts;

/// <summary>A listing as a source adapter sees it, before currency conversion, geocoding and de-duplication.</summary>
public sealed class RawListing
{
    public required string ExternalId { get; set; }
    public required string Url { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public double? Price { get; set; }
    public string? Currency { get; set; }
    public double? Rooms { get; set; }
    public double? AreaM2 { get; set; }
    public string? Floor { get; set; }
    public string? Address { get; set; }
    public string? District { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public bool GeoPrecise { get; set; }
    public bool? Pets { get; set; }
    public bool? Furnished { get; set; }
    public List<string> Images { get; set; } = [];
    public DateTime? PostedAt { get; set; }
}

/// <summary>What to scrape: country, city and how many result pages.</summary>
public sealed record ScrapeQuery(string Country, string City, int MaxPages, double? CityLat = null, double? CityLon = null);

/// <summary>
/// Command sent by the API to the queue <c>scrape-{source}</c>. Each worker instance listens only to
/// the queues of the sources it is configured for, so sources can be scaled and deployed per country.
/// </summary>
public sealed record ScrapeRequested(Guid RunId, int SearchId, string Source, ScrapeQuery Query);

/// <summary>Event published by a worker when a source run has finished, successfully or not.</summary>
public sealed record ScrapeCompleted(
    Guid RunId,
    int SearchId,
    string Source,
    IReadOnlyList<RawListing> Listings,
    string? Error,
    DateTime StartedAt,
    DateTime FinishedAt);

/// <summary>Geocode listings without coordinates and merge duplicates for a search.</summary>
public sealed record SearchEnrichmentRequested(int SearchId);

public static class Queues
{
    public static string Scrape(string source) => $"scrape-{source.Replace('_', '-')}";
    public const string Ingestion = "ingestion";
}
