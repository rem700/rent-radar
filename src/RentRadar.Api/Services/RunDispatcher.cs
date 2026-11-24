using MassTransit;
using Microsoft.EntityFrameworkCore;
using RentRadar.Api.Data;
using RentRadar.Contracts;
using RentRadar.Scraping;

namespace RentRadar.Api.Services;

/// <summary>
/// Starts a search: creates one <see cref="SourceRun"/> per matching source and sends a
/// <see cref="ScrapeRequested"/> command to that source's queue. Workers do the scraping; the results come
/// back as <see cref="ScrapeCompleted"/> events handled by the ingestion consumer.
/// </summary>
public sealed class RunDispatcher(
    RentRadarDb db,
    SourceCatalog catalog,
    GeocodingService geocoding,
    ISendEndpointProvider send,
    ILogger<RunDispatcher> logger)
{
    /// <summary>A run that has not reported back within this time is considered lost (no worker for the source).</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    public Task<bool> IsRunningAsync(int searchId, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - StaleAfter;
        return db.SourceRuns.AnyAsync(r => r.SearchId == searchId && r.FinishedAt == null && r.StartedAt > cutoff, ct);
    }

    public async Task<string> StartAsync(int searchId, CancellationToken ct = default)
    {
        var search = await db.Searches.FindAsync([searchId], ct);
        if (search is null) return "not_found";
        if (await IsRunningAsync(searchId, ct)) return "already_running";

        var adapters = catalog.For(search.Country, search.City, search.Sources);
        if (adapters.Count == 0) return "no_sources";

        if (search.CityLat is null && await geocoding.GeocodeAsync(search.City, search.Country, ct) is { } point)
            (search.CityLat, search.CityLon) = point;

        var now = DateTime.UtcNow;
        var runs = adapters.Select(a => new SourceRun { Id = Guid.NewGuid(), SearchId = search.Id, Source = a.Name, StartedAt = now }).ToList();
        db.SourceRuns.AddRange(runs);
        await db.SaveChangesAsync(ct);

        var query = new ScrapeQuery(search.Country, search.City, search.MaxPages, search.CityLat, search.CityLon);
        foreach (var run in runs)
        {
            var endpoint = await send.GetSendEndpoint(new Uri($"queue:{Queues.Scrape(run.Source)}"));
            await endpoint.Send(new ScrapeRequested(run.Id, search.Id, run.Source, query), ct);
        }
        logger.LogInformation("Search {SearchId} ({City}, {Country}) dispatched to {Sources}",
            search.Id, search.City, search.Country, string.Join(", ", runs.Select(r => r.Source)));
        return "started";
    }

    /// <summary>Closes runs that no worker answered, so the search does not look busy forever.</summary>
    public async Task<int> CloseStaleRunsAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - StaleAfter;
        return await db.SourceRuns
            .Where(r => r.FinishedAt == null && r.StartedAt <= cutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.FinishedAt, DateTime.UtcNow)
                .SetProperty(r => r.Error, "No worker answered within 30 minutes: is a worker for this source running?"), ct);
    }
}
