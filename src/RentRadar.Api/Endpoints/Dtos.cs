using Microsoft.AspNetCore.Mvc;
using RentRadar.Api.Data;
using RentRadar.Domain;

namespace RentRadar.Api.Endpoints;

// The JSON contract is snake_case and matches the React client (web/src/api.ts) one to one.

public sealed record SearchIn(
    string? Name,
    string Country,
    string City,
    SearchFilters? Filters,
    List<string>? Sources,
    int MaxPages = 3,
    bool AutoUpdate = true,
    int IntervalMinutes = 60)
{
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Country) || Country.Trim().Length != 2) return "country must be a two-letter ISO code";
        if (string.IsNullOrWhiteSpace(City)) return "city is required";
        if (MaxPages is < 1 or > 20) return "max_pages must be between 1 and 20";
        if (IntervalMinutes is < 10 or > 24 * 60) return "interval_minutes must be between 10 and 1440";
        return null;
    }
}

public sealed record SearchOut(
    int Id, string Name, string Country, string City, SearchFilters Filters, List<string>? Sources,
    int MaxPages, bool AutoUpdate, int IntervalMinutes, DateTime? LastRunAt, DateTime? LastViewedAt,
    double? CityLat, double? CityLon, bool Running, int Total, int NewCount);

public sealed record ListingOut(
    int Id, string Source, string Url, string Title, string? Description, double? Price, string? Currency,
    double? PriceEur, double? Rooms, double? AreaM2, string? Floor, string? Address, string? District,
    double? Lat, double? Lon, bool GeoPrecise, bool? Pets, bool? Furnished, List<string> Images,
    DateTime? PostedAt, DateTime FirstSeen, bool Active, List<PricePoint> PriceHistory)
{
    public static ListingOut From(Listing l) => new(
        l.Id, l.Source, l.Url, l.Title, l.Description, l.Price, l.Currency, l.PriceEur, l.Rooms, l.AreaM2, l.Floor,
        l.Address, l.District, l.Lat, l.Lon, l.GeoPrecise, l.Pets, l.Furnished, l.Images, l.PostedAt, l.FirstSeen,
        l.Active, l.PriceHistory);
}

public sealed record PropertyOut(int Id, bool Favorite, bool Hidden, string? Note, bool IsNew, ListingOut Main, List<ListingOut> Others);

public sealed record SourceRunOut(string Source, bool Ok, int Found, int New, string? Error, DateTime StartedAt, DateTime? FinishedAt);

public sealed record AdapterOut(string Name, string Title, IReadOnlyList<string> Countries, string Homepage);

public sealed record CurrencyOut(string Source, Dictionary<string, double> Rates);

public sealed record MetaOut(IReadOnlyDictionary<string, List<string>> Countries, List<AdapterOut> Adapters, CurrencyOut Currency);

/// <summary>Query string of GET /api/searches/{id}/properties: filters + view options.</summary>
public sealed class PropertyQuery
{
    [FromQuery(Name = "price_min")] public double? PriceMin { get; init; }
    [FromQuery(Name = "price_max")] public double? PriceMax { get; init; }
    [FromQuery(Name = "rooms_min")] public double? RoomsMin { get; init; }
    [FromQuery(Name = "rooms_max")] public double? RoomsMax { get; init; }
    [FromQuery(Name = "area_min")] public double? AreaMin { get; init; }
    [FromQuery(Name = "area_max")] public double? AreaMax { get; init; }
    [FromQuery(Name = "district")] public string? District { get; init; }
    [FromQuery(Name = "keywords")] public string? Keywords { get; init; }
    [FromQuery(Name = "exclude")] public string? Exclude { get; init; }
    [FromQuery(Name = "pets")] public bool? Pets { get; init; }
    [FromQuery(Name = "furnished")] public bool? Furnished { get; init; }
    [FromQuery(Name = "only_favorites")] public bool? OnlyFavorites { get; init; }
    [FromQuery(Name = "only_new")] public bool? OnlyNew { get; init; }
    [FromQuery(Name = "show_hidden")] public bool? ShowHidden { get; init; }
    [FromQuery(Name = "show_inactive")] public bool? ShowInactive { get; init; }
    [FromQuery(Name = "sort")] public string? Sort { get; init; }

    public SearchFilters ToFilters() => new()
    {
        PriceMin = PriceMin, PriceMax = PriceMax, RoomsMin = RoomsMin, RoomsMax = RoomsMax,
        AreaMin = AreaMin, AreaMax = AreaMax, District = District, Keywords = Keywords,
        Exclude = Exclude, Pets = Pets, Furnished = Furnished,
    };
}
