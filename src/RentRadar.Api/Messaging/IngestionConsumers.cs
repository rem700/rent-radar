using MassTransit;
using RentRadar.Api.Services;
using RentRadar.Contracts;

namespace RentRadar.Api.Messaging;

/// <summary>Stores what a worker found. Runs on the "ingestion" queue, one message at a time.</summary>
public sealed class ScrapeCompletedConsumer(IngestionService ingestion) : IConsumer<ScrapeCompleted>
{
    public async Task Consume(ConsumeContext<ScrapeCompleted> context)
    {
        var lastOpenRun = await ingestion.ApplyAsync(context.Message, context.CancellationToken);
        if (lastOpenRun) await context.Publish(new SearchEnrichmentRequested(context.Message.SearchId));
    }
}

/// <summary>Geocodes and merges duplicates in small batches; re-queues itself while work is left.</summary>
public sealed class SearchEnrichmentConsumer(IngestionService ingestion) : IConsumer<SearchEnrichmentRequested>
{
    public async Task Consume(ConsumeContext<SearchEnrichmentRequested> context)
    {
        if (await ingestion.EnrichAsync(context.Message.SearchId, context.CancellationToken))
            await context.Publish(context.Message);
    }
}

public static class IngestionBusExtensions
{
    public static void AddIngestionConsumers(this IBusRegistrationConfigurator x)
    {
        x.AddConsumer<ScrapeCompletedConsumer>().ExcludeFromConfigureEndpoints();
        x.AddConsumer<SearchEnrichmentConsumer>().ExcludeFromConfigureEndpoints();
    }

    /// <summary>
    /// Both consumers share one queue with a concurrency of one: writes for a search never race each other,
    /// and the geocoder's one-request-per-second limit holds across the process.
    /// </summary>
    public static void MapIngestionQueue(this IBusFactoryConfigurator cfg, IBusRegistrationContext ctx) =>
        cfg.ReceiveEndpoint(Queues.Ingestion, e =>
        {
            e.ConcurrentMessageLimit = 1;
            e.UseMessageRetry(r => r.Intervals(500, 2000, 5000));
            e.ConfigureConsumer<ScrapeCompletedConsumer>(ctx);
            e.ConfigureConsumer<SearchEnrichmentConsumer>(ctx);
        });
}
