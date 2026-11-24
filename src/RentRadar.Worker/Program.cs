using RentRadar.Scraping;
using RentRadar.ServiceDefaults;

// Scraper worker: listens to the scrape-{source} queues of the sources in Worker:Sources and publishes
// ScrapeCompleted events. It has no database; several workers (for example one per country) can run side by side.
var builder = WebApplication.CreateBuilder(args);
// machine-specific settings (e.g. Mail:IniPath), git-ignored
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
builder.AddServiceDefaults();
builder.Services.AddScraping(builder.Configuration);

var sources = ScraperBusExtensions.ResolveSources(builder.Configuration, ScraperBusExtensions.KnownSources);
builder.Services.AddMessaging(builder.Configuration,
    x => x.AddScraperConsumer(),
    (ctx, cfg) => cfg.MapScraperQueues(ctx, sources));

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/", () => Results.Ok(new { service = "rent-radar-worker", sources }));
app.Logger.LogInformation("Worker serves sources: {Sources}", string.Join(", ", sources));
app.Run();
