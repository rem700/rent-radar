using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using RentRadar.Api;
using RentRadar.Api.Data;
using RentRadar.Api.Endpoints;
using RentRadar.Api.Messaging;
using RentRadar.Api.Services;
using RentRadar.Scraping;
using RentRadar.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
// machine-specific settings (e.g. Mail:IniPath), git-ignored
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
builder.AddServiceDefaults();
var config = builder.Configuration;

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// storage: PostgreSQL in docker compose, SQLite for the single-process mode
builder.Services.AddDbContext<RentRadarDb>(o =>
{
    if (config.UsePostgres()) o.UseNpgsql(config.GetConnectionString("postgres"));
    else o.UseSqlite(config.GetConnectionString("sqlite") ?? "Data Source=data/rent-radar.db");
});

// cache: HybridCache in memory, with Redis as the shared second level when configured
builder.Services.AddHybridCache();
if (config.GetConnectionString("redis") is { Length: > 0 } redis)
    builder.Services.AddStackExchangeRedisCache(o => { o.Configuration = redis; o.InstanceName = "rent-radar:"; });
else
    builder.Services.AddDistributedMemoryCache();

builder.Services.Configure<CurrencyOptions>(config.GetSection(CurrencyOptions.Section));
builder.Services.Configure<GeocodingOptions>(config.GetSection(GeocodingOptions.Section));
builder.Services.Configure<SchedulerOptions>(config.GetSection(SchedulerOptions.Section));
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<GeocodingService>((sp, c) =>
{
    var o = config.GetSection(GeocodingOptions.Section).Get<GeocodingOptions>() ?? new GeocodingOptions();
    c.BaseAddress = new Uri(o.BaseUrl);
    c.DefaultRequestHeaders.UserAgent.ParseAdd(o.UserAgent);
    c.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<CurrencyService>();
builder.Services.AddScoped<RunDispatcher>();
builder.Services.AddScoped<IngestionService>();
builder.Services.AddHostedService<SchedulerService>();

// the source catalog (names, countries) is needed even when scraping runs in separate workers
builder.Services.AddScraping(config);

// Workers:InProcess = true runs the scrapers inside the API (single-process mode, no broker needed)
var inProcessWorkers = config.GetValue("Workers:InProcess", false);
var workerSources = ScraperBusExtensions.ResolveSources(config, ScraperBusExtensions.KnownSources);
builder.Services.AddMessaging(config,
    x =>
    {
        x.AddIngestionConsumers();
        if (inProcessWorkers) x.AddScraperConsumer();
    },
    (ctx, cfg) =>
    {
        cfg.MapIngestionQueue(ctx);
        if (inProcessWorkers) cfg.MapScraperQueues(ctx, workerSources);
    });

builder.Services.AddHealthChecks()
    .AddDbContextCheck<RentRadarDb>("database", tags: ["ready"])
    .AddCheck<DistributedCacheHealthCheck>("cache", tags: ["ready"]);

var app = builder.Build();

await app.InitializeDatabaseAsync();

app.UseExceptionHandler();
app.MapOpenApi();
app.MapDefaultEndpoints();
app.MapApi();

// single-process mode: serve the built React app (web/dist) from the API
var webRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, config["Web:Root"] ?? "../../web/dist"));
if (File.Exists(Path.Combine(webRoot, "index.html")))
{
    var files = new PhysicalFileProvider(webRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
    app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = files });
}

app.Run();

public partial class Program;
