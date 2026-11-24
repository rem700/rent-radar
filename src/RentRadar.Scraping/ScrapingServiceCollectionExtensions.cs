using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RentRadar.Scraping.Adapters;

namespace RentRadar.Scraping;

public static class ScrapingServiceCollectionExtensions
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

    /// <summary>Registers every source adapter, the catalog and the HTTP client they share.</summary>
    public static IServiceCollection AddScraping(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MailOptions>(configuration.GetSection(MailOptions.Section));

        services.AddHttpClient(SourceAdapter.HttpClientName, c =>
            {
                c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9,hu;q=0.8,ro;q=0.7,sk;q=0.6");
            })
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(20);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            });

        // one line per site
        services.AddSingleton<ISourceAdapter, JofogasAdapter>();
        services.AddSingleton<ISourceAdapter, IngatlannetAdapter>();
        services.AddSingleton<ISourceAdapter, AlberletAdapter>();
        services.AddSingleton<ISourceAdapter, IngatlanMailAdapter>();
        services.AddSingleton<ISourceAdapter, StoriaAdapter>();
        services.AddSingleton<ISourceAdapter, OlxRoAdapter>();
        services.AddSingleton<ISourceAdapter, NehnutelnostiAdapter>();
        services.AddSingleton<ISourceAdapter, DemoAdapter>();

        services.AddSingleton<SourceCatalog>();
        services.AddSingleton<ScrapeMetrics>();
        return services;
    }
}
