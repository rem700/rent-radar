using MassTransit;
using Microsoft.Extensions.Configuration;
using RentRadar.Contracts;

namespace RentRadar.Scraping;

public static class ScraperBusExtensions
{
    /// <summary>The scrape consumer is bound to explicit per-source queues, not to an automatic endpoint.</summary>
    public static void AddScraperConsumer(this IBusRegistrationConfigurator x) =>
        x.AddConsumer<ScrapeRequestedConsumer>().ExcludeFromConfigureEndpoints();

    /// <summary>
    /// One queue per source (<c>scrape-jofogas</c>, <c>scrape-storia</c>, …), each processing one message at a time,
    /// so a site is never hit by parallel runs from the same worker.
    /// </summary>
    public static void MapScraperQueues(this IBusFactoryConfigurator cfg, IBusRegistrationContext ctx, IEnumerable<string> sources)
    {
        foreach (var source in sources)
            cfg.ReceiveEndpoint(Queues.Scrape(source), e =>
            {
                e.ConcurrentMessageLimit = 1;
                e.ConfigureConsumer<ScrapeRequestedConsumer>(ctx);
            });
    }

    /// <summary><c>Worker:Sources</c>: "*" (default) or a comma-separated list of source names.</summary>
    public static IReadOnlyList<string> ResolveSources(IConfiguration configuration, IEnumerable<string> known)
    {
        var raw = configuration["Worker:Sources"];
        var all = known.ToList();
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "*") return all;
        var wanted = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unknown = wanted.Except(all).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException($"Worker:Sources has unknown sources: {string.Join(", ", unknown)}. Known: {string.Join(", ", all)}");
        return wanted;
    }

    /// <summary>Source names without building the service provider (needed while configuring the bus).</summary>
    public static readonly string[] KnownSources =
        ["jofogas", "ingatlannet", "alberlet", "ingatlan", "storia", "olx_ro", "nehnutelnosti", "demo"];
}
