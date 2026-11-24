using System.Text.RegularExpressions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using RentRadar.Api.Data;
using RentRadar.Contracts;
using RentRadar.Domain;

namespace RentRadar.Api.Services;

/// <summary>Turns worker results into listings: upsert, price history, removed-listing flags, de-duplication, geocoding.</summary>
public sealed partial class IngestionService(
    RentRadarDb db,
    CurrencyService currency,
    GeocodingService geocoding,
    Microsoft.Extensions.Options.IOptions<GeocodingOptions> geoOptions,
    ILogger<IngestionService> logger)
{
    /// <summary>Applies one <see cref="ScrapeCompleted"/>. Returns true when it was the last open run of the search.</summary>
    public async Task<bool> ApplyAsync(ScrapeCompleted msg, CancellationToken ct)
    {
        var run = await db.SourceRuns.FindAsync([msg.RunId], ct);
        if (run is null || run.FinishedAt is not null) return false; // unknown or already applied (redelivery)
        var search = await db.Searches.FindAsync([msg.SearchId], ct);
        if (search is null) return false;

        var rates = (await currency.GetRatesAsync(ct)).Rates;
        var existing = await db.Listings
            .Where(l => l.SearchId == msg.SearchId && l.Source == msg.Source)
            .ToDictionaryAsync(l => l.ExternalId, ct);

        var now = DateTime.UtcNow;
        var added = new List<Listing>();
        var found = 0;
        foreach (var raw in msg.Listings.DistinctBy(l => l.ExternalId))
        {
            found++;
            if (existing.TryGetValue(raw.ExternalId, out var listing)) Update(listing, raw, rates, now);
            else added.Add(Create(msg.SearchId, msg.Source, raw, rates, now));
        }
        db.Listings.AddRange(added);

        run.Ok = msg.Error is null;
        run.Error = msg.Error is { Length: > 4000 } e ? e[..4000] : msg.Error;
        run.Found = found;
        run.New = added.Count;
        run.FinishedAt = now;
        await db.SaveChangesAsync(ct);

        // listings a healthy source no longer shows are marked as removed (everything seen in this pass has LastSeen = now)
        var dispatchedAt = run.StartedAt;
        if (run.Ok && found > 0)
            await db.Listings
                .Where(l => l.SearchId == msg.SearchId && l.Source == msg.Source && l.LastSeen < dispatchedAt && l.Active)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.Active, false), ct);

        foreach (var listing in added) await AssignPropertyAsync(listing, ct);
        await db.SaveChangesAsync(ct);

        var lastOpen = !await db.SourceRuns.AnyAsync(r => r.SearchId == msg.SearchId && r.FinishedAt == null, ct);
        if (lastOpen)
        {
            search.LastRunAt = now;
            await db.SaveChangesAsync(ct);
        }
        logger.LogInformation("Search {SearchId}: {Source} brought {Found} listings, {New} new", msg.SearchId, msg.Source, found, added.Count);
        return lastOpen;
    }

    private static Listing Create(int searchId, string source, RawListing raw, IReadOnlyDictionary<string, double> rates, DateTime now) => new()
    {
        SearchId = searchId,
        Source = source,
        ExternalId = raw.ExternalId,
        Url = raw.Url,
        Title = raw.Title.Length > 500 ? raw.Title[..500] : raw.Title,
        Description = raw.Description,
        Price = raw.Price,
        Currency = raw.Currency,
        PriceEur = Currencies.ToEur(raw.Price, raw.Currency, rates),
        Rooms = raw.Rooms,
        AreaM2 = raw.AreaM2,
        Floor = raw.Floor,
        Address = raw.Address,
        District = raw.District,
        Lat = raw.Lat,
        Lon = raw.Lon,
        GeoPrecise = raw.GeoPrecise,
        GeocodeAttempted = raw.Lat is not null,
        Pets = raw.Pets,
        Furnished = raw.Furnished,
        Images = raw.Images,
        PostedAt = raw.PostedAt,
        FirstSeen = now,
        LastSeen = now,
        Active = true,
    };

    private static void Update(Listing l, RawListing raw, IReadOnlyDictionary<string, double> rates, DateTime now)
    {
        if (raw.Price is not null && l.Price is not null && Math.Abs(raw.Price.Value - l.Price.Value) > 0.001)
            l.PriceHistory = [.. l.PriceHistory, new PricePoint(now, l.Price.Value)];
        l.Url = raw.Url;
        l.Title = raw.Title.Length > 500 ? raw.Title[..500] : raw.Title;
        l.Description = raw.Description ?? l.Description;
        l.Price = raw.Price ?? l.Price;
        l.Currency = raw.Currency ?? l.Currency;
        l.Rooms = raw.Rooms ?? l.Rooms;
        l.AreaM2 = raw.AreaM2 ?? l.AreaM2;
        l.Floor = raw.Floor ?? l.Floor;
        l.District = raw.District ?? l.District;
        l.Pets = raw.Pets ?? l.Pets;
        l.Furnished = raw.Furnished ?? l.Furnished;
        l.PostedAt = raw.PostedAt ?? l.PostedAt;
        if (raw.Images.Count > 0) l.Images = raw.Images;
        if (raw.Lat is not null)
        {
            (l.Lat, l.Lon, l.GeoPrecise, l.GeocodeAttempted) = (raw.Lat, raw.Lon, raw.GeoPrecise, true);
        }
        if (!string.IsNullOrEmpty(raw.Address) && raw.Address != l.Address)
        {
            l.Address = raw.Address;
            if (raw.Lat is null) (l.Lat, l.Lon, l.GeoPrecise, l.GeocodeAttempted) = (null, null, false, false);
        }
        l.PriceEur = Currencies.ToEur(l.Price, l.Currency, rates);
        l.LastSeen = now;
        l.Active = true;
    }

    /// <summary>Attaches a new listing to the property of a duplicate from another source, or to a new property.</summary>
    private async Task AssignPropertyAsync(Listing listing, CancellationToken ct)
    {
        if (listing.PropertyId is not null) return;
        var match = await FindDuplicateAsync(listing, ct);
        if (match is not null)
        {
            listing.PropertyId = match.PropertyId;
            return;
        }
        listing.Property = new Property();
    }

    private async Task<Listing?> FindDuplicateAsync(Listing listing, CancellationToken ct)
    {
        if (listing.AreaM2 is not > 0 || listing.PriceEur is not > 0) return null;
        var (lo, hi) = (listing.AreaM2.Value - Dedup.AreaTolerance, listing.AreaM2.Value + Dedup.AreaTolerance);
        var candidates = await db.Listings
            .Where(l => l.SearchId == listing.SearchId && l.Id != listing.Id && l.Source != listing.Source
                        && l.PropertyId != null && l.AreaM2 >= lo && l.AreaM2 <= hi)
            .ToListAsync(ct);
        return candidates.FirstOrDefault(c => c.PropertyId != listing.PropertyId && Dedup.IsDuplicate(listing, c));
    }

    [GeneratedRegex(@"\b[IVXL]+\.\s*kerület\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanDistrict();

    [GeneratedRegex(@"(,\s*)+")]
    private static partial Regex Commas();

    /// <summary>
    /// Geocodes listings without coordinates (by address) and merges newly placed listings into duplicates.
    /// Returns true when more listings are left for another pass.
    /// </summary>
    public async Task<bool> EnrichAsync(int searchId, CancellationToken ct)
    {
        var search = await db.Searches.FindAsync([searchId], ct);
        if (search is null || !geoOptions.Value.Enabled) return false;

        var batch = await db.Listings
            .Where(l => l.SearchId == searchId && !l.GeocodeAttempted && l.Lat == null && l.Address != null)
            .OrderByDescending(l => l.FirstSeen)
            .Take(geoOptions.Value.MaxPerMessage)
            .ToListAsync(ct);

        var placed = new List<Listing>();
        foreach (var l in batch)
        {
            // Nominatim handles "XIII. kerület" poorly: drop it, keep the city
            var q = Commas().Replace(RomanDistrict().Replace(l.Address!, ""), ", ").Trim(' ', ',');
            if (!q.Contains(search.City, StringComparison.OrdinalIgnoreCase)) q = $"{q}, {search.City}";
            if (await geocoding.GeocodeAsync(q, search.Country, ct) is { } p)
            {
                // a street-level point without a house number: approximate
                (l.Lat, l.Lon, l.GeoPrecise) = (p.Lat, p.Lon, false);
                placed.Add(l);
            }
            l.GeocodeAttempted = true;
        }
        await db.SaveChangesAsync(ct);

        await MergePlacedAsync(placed, ct);
        await db.SaveChangesAsync(ct);

        return batch.Count == geoOptions.Value.MaxPerMessage &&
               await db.Listings.AnyAsync(l => l.SearchId == searchId && !l.GeocodeAttempted && l.Lat == null && l.Address != null, ct);
    }

    /// <summary>A listing that just got coordinates may now match a duplicate; merge it if its own property is a fresh singleton.</summary>
    private async Task MergePlacedAsync(List<Listing> placed, CancellationToken ct)
    {
        foreach (var l in placed)
        {
            if (l.PropertyId is not { } ownId) continue;
            var siblings = await db.Listings.CountAsync(x => x.PropertyId == ownId, ct);
            if (siblings > 1) continue;
            var own = await db.Properties.FindAsync([ownId], ct);
            if (own is null || own.HasUserState) continue;
            var match = await FindDuplicateAsync(l, ct);
            if (match?.PropertyId is not { } targetId) continue;
            l.PropertyId = targetId;
            await db.SaveChangesAsync(ct);
            db.Properties.Remove(own);
        }
    }
}
