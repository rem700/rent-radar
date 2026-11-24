using System.Diagnostics;
using System.Diagnostics.Metrics;
using MassTransit;
using Microsoft.Extensions.Logging;
using RentRadar.Contracts;

namespace RentRadar.Scraping;

/// <summary>
/// Runs one source for one search and publishes the result. A failing site never throws out of the
/// consumer: the error travels back in <see cref="ScrapeCompleted.Error"/> and shows up in the UI.
/// </summary>
public sealed class ScrapeRequestedConsumer(
    SourceCatalog catalog,
    ScrapeMetrics metrics,
    ILogger<ScrapeRequestedConsumer> logger) : IConsumer<ScrapeRequested>
{
    public const int MaxListingsPerRun = 2000;

    public async Task Consume(ConsumeContext<ScrapeRequested> context)
    {
        var msg = context.Message;
        var started = DateTime.UtcNow;
        var timer = Stopwatch.StartNew();
        var listings = new List<RawListing>();
        string? error = null;

        using var activity = ScrapeMetrics.ActivitySource.StartActivity($"scrape {msg.Source}");
        activity?.SetTag("rentradar.source", msg.Source);
        activity?.SetTag("rentradar.search_id", msg.SearchId);
        activity?.SetTag("rentradar.city", msg.Query.City);

        try
        {
            var adapter = catalog.Get(msg.Source) ?? throw new AdapterException($"this worker does not know the source \"{msg.Source}\"");
            await foreach (var item in adapter.FetchAsync(msg.Query, context.CancellationToken))
            {
                listings.Add(item);
                if (listings.Count >= MaxListingsPerRun) break;
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw; // shutting down: let the broker redeliver
        }
        catch (Exception ex)
        {
            error = ex is AdapterException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
            logger.LogWarning(ex, "Source {Source} failed for search {SearchId}", msg.Source, msg.SearchId);
            activity?.SetStatus(ActivityStatusCode.Error, error);
        }

        metrics.Record(msg.Source, listings.Count, error is null, timer.Elapsed);
        logger.LogInformation("Source {Source} found {Count} listings for search {SearchId} in {Elapsed:0.0}s",
            msg.Source, listings.Count, msg.SearchId, timer.Elapsed.TotalSeconds);

        await context.Publish(new ScrapeCompleted(msg.RunId, msg.SearchId, msg.Source, listings, error, started, DateTime.UtcNow));
    }
}

/// <summary>Custom telemetry of the scrapers: meter and activity source "RentRadar.Scraping".</summary>
public sealed class ScrapeMetrics
{
    public const string Name = "RentRadar.Scraping";
    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _listings;
    private readonly Counter<long> _runs;
    private readonly Histogram<double> _duration;

    public ScrapeMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);
        _listings = meter.CreateCounter<long>("rentradar.scrape.listings", description: "Listings returned by sources");
        _runs = meter.CreateCounter<long>("rentradar.scrape.runs", description: "Source runs by outcome");
        _duration = meter.CreateHistogram<double>("rentradar.scrape.duration", unit: "s", description: "Time to scrape one source");
    }

    public void Record(string source, int count, bool ok, TimeSpan elapsed)
    {
        var tag = new KeyValuePair<string, object?>("source", source);
        _listings.Add(count, tag);
        _runs.Add(1, tag, new KeyValuePair<string, object?>("outcome", ok ? "ok" : "error"));
        _duration.Record(elapsed.TotalSeconds, tag);
    }
}
