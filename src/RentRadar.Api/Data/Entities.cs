using RentRadar.Domain;

namespace RentRadar.Api.Data;

/// <summary>A saved search: region + filters. Sources are picked automatically by country.</summary>
public sealed class Search
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Country { get; set; }
    public required string City { get; set; }
    public SearchFilters Filters { get; set; } = new();
    /// <summary>null = every source that covers the country.</summary>
    public List<string>? Sources { get; set; }
    public int MaxPages { get; set; } = 3;
    public bool AutoUpdate { get; set; } = true;
    public int IntervalMinutes { get; set; } = 60;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastRunAt { get; set; }
    public DateTime? LastViewedAt { get; set; }
    public double? CityLat { get; set; }
    public double? CityLon { get; set; }
}

/// <summary>One flat: a group of duplicate listings from different sites.</summary>
public sealed class Property
{
    public int Id { get; set; }
    public bool Favorite { get; set; }
    public bool Hidden { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool HasUserState => Favorite || Hidden || !string.IsNullOrEmpty(Note);
}

public sealed record PricePoint(DateTime At, double Price);

public sealed class Listing : IDedupFacts, IFilterableListing
{
    public int Id { get; set; }
    public int SearchId { get; set; }
    public int? PropertyId { get; set; }
    public Property? Property { get; set; }
    public required string Source { get; set; }
    public required string ExternalId { get; set; }
    public required string Url { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public double? Price { get; set; }
    public string? Currency { get; set; }
    public double? PriceEur { get; set; }
    public double? Rooms { get; set; }
    public double? AreaM2 { get; set; }
    public string? Floor { get; set; }
    public string? Address { get; set; }
    public string? District { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public bool GeoPrecise { get; set; }
    /// <summary>Set once the address has been sent to the geocoder, so un-geocodable addresses are not retried forever.</summary>
    public bool GeocodeAttempted { get; set; }
    public bool? Pets { get; set; }
    public bool? Furnished { get; set; }
    public List<string> Images { get; set; } = [];
    public DateTime? PostedAt { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public bool Active { get; set; } = true;
    public List<PricePoint> PriceHistory { get; set; } = [];
}

/// <summary>One source run for one search — shows which site broke and why.</summary>
public sealed class SourceRun
{
    public Guid Id { get; set; }
    public int SearchId { get; set; }
    public required string Source { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public bool Ok { get; set; }
    public int Found { get; set; }
    public int New { get; set; }
    public string? Error { get; set; }
}

public sealed class GeoCacheEntry
{
    public required string Query { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
