using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RentRadar.Api.Data;
using RentRadar.Api.Services;
using RentRadar.Scraping;

namespace RentRadar.Api.Endpoints;

public static class ApiEndpoints
{
    private static readonly HashSet<string> Sorts = ["newest", "price_asc", "price_desc", "area_desc", "eur_m2"];

    public static void MapApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/meta", Meta);
        api.MapGet("/searches", ListSearches);
        api.MapPost("/searches", CreateSearch);
        api.MapPut("/searches/{id:int}", UpdateSearch);
        api.MapDelete("/searches/{id:int}", DeleteSearch);
        api.MapPost("/searches/{id:int}/run", Run);
        api.MapPost("/searches/{id:int}/viewed", MarkViewed);
        api.MapGet("/searches/{id:int}/runs", Runs);
        api.MapGet("/searches/{id:int}/properties", ListProperties);
        api.MapPatch("/properties/{id:int}", PatchProperty);
    }

    private static IResult Problem(int status, string detail) => Results.Problem(detail: detail, statusCode: status);
    private static IResult NotFound() => Problem(404, "Not found");

    private static async Task<MetaOut> Meta(SourceCatalog catalog, CurrencyService currency, CancellationToken ct)
    {
        var rates = await currency.GetRatesAsync(ct);
        return new MetaOut(
            catalog.SupportedCountries(),
            catalog.All.Select(a => new AdapterOut(a.Name, a.Title, a.Countries, a.Homepage)).ToList(),
            new CurrencyOut(rates.Source, rates.Rates));
    }

    private static async Task<SearchOut> ToOut(RentRadarDb db, RunDispatcher dispatcher, Search s, CancellationToken ct)
    {
        var since = s.LastViewedAt ?? s.CreatedAt;
        var active = db.Listings.Where(l => l.SearchId == s.Id && l.Active && l.PropertyId != null);
        var total = await active.Select(l => l.PropertyId).Distinct().CountAsync(ct);
        var fresh = await active.Where(l => l.FirstSeen > since).Select(l => l.PropertyId).Distinct().CountAsync(ct);
        return new SearchOut(s.Id, s.Name, s.Country, s.City, s.Filters, s.Sources, s.MaxPages, s.AutoUpdate,
            s.IntervalMinutes, s.LastRunAt, s.LastViewedAt, s.CityLat, s.CityLon,
            await dispatcher.IsRunningAsync(s.Id, ct), total, fresh);
    }

    private static async Task<List<SearchOut>> ListSearches(RentRadarDb db, RunDispatcher dispatcher, CancellationToken ct)
    {
        var result = new List<SearchOut>();
        foreach (var s in await db.Searches.AsNoTracking().OrderByDescending(s => s.Id).ToListAsync(ct))
            result.Add(await ToOut(db, dispatcher, s, ct));
        return result;
    }

    private static async Task<IResult> CreateSearch(SearchIn body, RentRadarDb db, SourceCatalog catalog, RunDispatcher dispatcher, CancellationToken ct)
    {
        if (body.Validate() is { } error) return Problem(422, error);
        var country = body.Country.Trim().ToUpperInvariant();
        var city = body.City.Trim();
        if (catalog.For(country, city, body.Sources).Count == 0) return Problem(400, $"No sources for country {country}");

        var search = new Search
        {
            Name = string.IsNullOrWhiteSpace(body.Name) ? $"{city} ({country})" : body.Name.Trim(),
            Country = country,
            City = city,
            Filters = body.Filters ?? new(),
            Sources = body.Sources,
            MaxPages = body.MaxPages,
            AutoUpdate = body.AutoUpdate,
            IntervalMinutes = body.IntervalMinutes,
        };
        db.Searches.Add(search);
        await db.SaveChangesAsync(ct);
        await dispatcher.StartAsync(search.Id, ct);
        return Results.Ok(await ToOut(db, dispatcher, search, ct));
    }

    private static async Task<IResult> UpdateSearch(int id, SearchIn body, RentRadarDb db, RunDispatcher dispatcher, CancellationToken ct)
    {
        if (body.Validate() is { } error) return Problem(422, error);
        var s = await db.Searches.FindAsync([id], ct);
        if (s is null) return NotFound();
        var country = body.Country.Trim().ToUpperInvariant();
        var city = body.City.Trim();
        if (s.City != city || s.Country != country) (s.CityLat, s.CityLon) = (null, null);
        s.Name = string.IsNullOrWhiteSpace(body.Name) ? s.Name : body.Name.Trim();
        (s.Country, s.City) = (country, city);
        s.Filters = body.Filters ?? new();
        s.Sources = body.Sources;
        (s.MaxPages, s.AutoUpdate, s.IntervalMinutes) = (body.MaxPages, body.AutoUpdate, body.IntervalMinutes);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToOut(db, dispatcher, s, ct));
    }

    private static async Task<IResult> DeleteSearch(int id, RentRadarDb db, CancellationToken ct)
    {
        if (!await db.Searches.AnyAsync(s => s.Id == id, ct)) return NotFound();
        var propertyIds = await db.Listings.Where(l => l.SearchId == id && l.PropertyId != null)
            .Select(l => l.PropertyId!.Value).Distinct().ToListAsync(ct);
        await db.Listings.Where(l => l.SearchId == id).ExecuteDeleteAsync(ct);
        await db.SourceRuns.Where(r => r.SearchId == id).ExecuteDeleteAsync(ct);
        await db.Searches.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
        await db.Properties
            .Where(p => propertyIds.Contains(p.Id) && !db.Listings.Any(l => l.PropertyId == p.Id))
            .ExecuteDeleteAsync(ct);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> Run(int id, RunDispatcher dispatcher, CancellationToken ct) =>
        await dispatcher.StartAsync(id, ct) switch
        {
            "not_found" => NotFound(),
            var status => Results.Ok(new { status }),
        };

    private static async Task<IResult> MarkViewed(int id, RentRadarDb db, CancellationToken ct)
    {
        var s = await db.Searches.FindAsync([id], ct);
        if (s is null) return NotFound();
        s.LastViewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { ok = true });
    }

    private static async Task<List<SourceRunOut>> Runs(int id, RentRadarDb db, CancellationToken ct)
    {
        var rows = await db.SourceRuns.AsNoTracking().Where(r => r.SearchId == id)
            .OrderByDescending(r => r.StartedAt).Take(30).ToListAsync(ct);
        return rows.GroupBy(r => r.Source)
            .Select(g => g.First())
            .Select(r => new SourceRunOut(r.Source, r.Ok, r.Found, r.New, r.Error, r.StartedAt, r.FinishedAt))
            .ToList();
    }

    private static async Task<IResult> ListProperties(int id, [AsParameters] PropertyQuery q, RentRadarDb db, CancellationToken ct)
    {
        var search = await db.Searches.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (search is null) return NotFound();
        var sort = q.Sort ?? "newest";
        if (!Sorts.Contains(sort)) return Problem(422, $"sort must be one of: {string.Join(", ", Sorts)}");

        var filters = q.ToFilters();
        var since = search.LastViewedAt ?? search.CreatedAt;
        var listings = db.Listings.AsNoTracking().Where(l => l.SearchId == id && l.PropertyId != null);
        if (q.ShowInactive != true) listings = listings.Where(l => l.Active);
        var groups = (await listings.ToListAsync(ct)).GroupBy(l => l.PropertyId!.Value).ToList();
        var ids = groups.Select(g => g.Key).ToList();
        var props = await db.Properties.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var result = new List<PropertyOut>();
        foreach (var g in groups)
        {
            if (!props.TryGetValue(g.Key, out var p)) continue;
            if (p.Hidden && q.ShowHidden != true) continue;
            if (q.OnlyFavorites == true && !p.Favorite) continue;
            // the cheapest offer represents the flat
            var ordered = g.OrderBy(l => l.PriceEur is null).ThenBy(l => l.PriceEur ?? 0).ToList();
            var main = ordered[0];
            if (!filters.Matches(main)) continue;
            var isNew = ordered.Min(l => l.FirstSeen) > since;
            if (q.OnlyNew == true && !isNew) continue;
            result.Add(new PropertyOut(p.Id, p.Favorite, p.Hidden, p.Note, isNew, ListingOut.From(main),
                ordered.Skip(1).Select(ListingOut.From).ToList()));
        }

        Func<PropertyOut, double> key = sort switch
        {
            "price_asc" => o => o.Main.PriceEur ?? 1e12,
            "price_desc" => o => -(o.Main.PriceEur ?? 0),
            "area_desc" => o => -(o.Main.AreaM2 ?? 0),
            "eur_m2" => o => o.Main is { PriceEur: > 0, AreaM2: > 0 } ? o.Main.PriceEur.Value / o.Main.AreaM2.Value : 1e12,
            _ => o => -(o.Main.PostedAt ?? o.Main.FirstSeen).Ticks,
        };
        return Results.Ok(result.OrderBy(key).ToList());
    }

    private static async Task<IResult> PatchProperty(int id, JsonObject body, RentRadarDb db, CancellationToken ct)
    {
        var p = await db.Properties.FindAsync([id], ct);
        if (p is null) return NotFound();
        try
        {
            if (body.TryGetPropertyValue("favorite", out var favorite) && favorite is not null) p.Favorite = favorite.GetValue<bool>();
            if (body.TryGetPropertyValue("hidden", out var hidden) && hidden is not null) p.Hidden = hidden.GetValue<bool>();
            if (body.TryGetPropertyValue("note", out var note)) p.Note = note?.GetValue<string>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return Problem(422, "favorite and hidden must be booleans, note a string or null");
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { ok = true });
    }
}
