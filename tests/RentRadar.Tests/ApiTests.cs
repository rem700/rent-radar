using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RentRadar.Scraping;

namespace RentRadar.Tests;

/// <summary>
/// End to end in one process: API + in-memory MassTransit transport + in-process demo worker + SQLite.
/// Creating a search dispatches a scrape command, the worker answers with an event, ingestion stores
/// the listings and the API serves them in the snake_case contract the React client expects.
/// </summary>
public sealed class ApiTests : IClassFixture<ApiTests.App>
{
    public sealed class App : WebApplicationFactory<Program>
    {
        private readonly string _db = Path.Combine(Path.GetTempPath(), $"rent-radar-test-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:sqlite", $"Data Source={_db}");
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("Messaging:Transport", "InMemory");
            builder.UseSetting("Workers:InProcess", "true");
            builder.UseSetting("Worker:Sources", "demo");
            builder.UseSetting("Scheduler:Enabled", "false");
            builder.UseSetting("Currency:Online", "false");
            builder.UseSetting("Geocoding:Enabled", "false");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { File.Delete(_db); } catch (IOException) { }
        }
    }

    private readonly HttpClient _http;
    public ApiTests(App app) => _http = app.CreateClient();

    [Fact]
    public async Task Meta_lists_countries_sources_and_rates()
    {
        var meta = await _http.GetFromJsonAsync<JsonObject>("/api/meta");
        var countries = meta!["countries"]!.AsObject();
        Assert.Contains("HU", countries.Select(c => c.Key));
        Assert.Contains("RO", countries.Select(c => c.Key));
        Assert.Contains("SK", countries.Select(c => c.Key));
        Assert.Equal("fallback", meta["currency"]!["source"]!.GetValue<string>());
        Assert.Equal(8, meta["adapters"]!.AsArray().Count);
    }

    [Fact]
    public async Task Search_flows_through_the_bus_and_returns_properties()
    {
        var created = await _http.PostAsJsonAsync("/api/searches", new
        {
            country = "xx",
            city = "Testville",
            filters = new { price_max = 5000 },
            sources = (string[]?)null,
            max_pages = 1,
            auto_update = false,
            interval_minutes = 60,
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var search = (await created.Content.ReadFromJsonAsync<JsonObject>())!;
        var id = search["id"]!.GetValue<int>();
        Assert.Equal("XX", search["country"]!.GetValue<string>());
        Assert.Equal(5000, search["filters"]!["price_max"]!.GetValue<double>());

        JsonArray properties = [];
        for (var i = 0; i < 60 && properties.Count == 0; i++)
        {
            await Task.Delay(500);
            properties = (await _http.GetFromJsonAsync<JsonArray>($"/api/searches/{id}/properties?sort=price_asc"))!;
        }
        Assert.Equal(30, properties.Count);
        var first = properties[0]!;
        Assert.True(first["is_new"]!.GetValue<bool>());
        Assert.Equal("demo", first["main"]!["source"]!.GetValue<string>());
        Assert.NotNull(first["main"]!["price_eur"]);
        var prices = properties.Select(p => p!["main"]!["price_eur"]!.GetValue<double>()).ToList();
        Assert.Equal(prices.OrderBy(p => p), prices);

        var runs = (await _http.GetFromJsonAsync<JsonArray>($"/api/searches/{id}/runs"))!;
        var run = Assert.Single(runs)!;
        Assert.True(run["ok"]!.GetValue<bool>());
        Assert.Equal(30, run["found"]!.GetValue<int>());

        // filters are applied on read
        var cheap = (await _http.GetFromJsonAsync<JsonArray>($"/api/searches/{id}/properties?price_max={prices[4].ToString(System.Globalization.CultureInfo.InvariantCulture)}"))!;
        Assert.Equal(5, cheap.Count);

        // favourites
        var propertyId = first["id"]!.GetValue<int>();
        var patch = await _http.PatchAsJsonAsync($"/api/properties/{propertyId}", new { favorite = true, note = "call owner" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var favorites = (await _http.GetFromJsonAsync<JsonArray>($"/api/searches/{id}/properties?only_favorites=true"))!;
        Assert.Equal("call owner", Assert.Single(favorites)!["note"]!.GetValue<string>());

        // "viewed" clears the new badge
        await _http.PostAsync($"/api/searches/{id}/viewed", null);
        var list = (await _http.GetFromJsonAsync<JsonArray>("/api/searches"))!;
        var mine = list.Single(s => s!["id"]!.GetValue<int>() == id)!;
        Assert.Equal(0, mine["new_count"]!.GetValue<int>());
        Assert.Equal(30, mine["total"]!.GetValue<int>());
        Assert.False(mine["running"]!.GetValue<bool>());
        Assert.EndsWith("Z", mine["last_run_at"]!.GetValue<string>());

        var deleted = await _http.DeleteAsync($"/api/searches/{id}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/searches/{id}/properties")).StatusCode);
    }

    [Fact]
    public async Task Invalid_search_returns_problem_detail()
    {
        var response = await _http.PostAsJsonAsync("/api/searches", new { country = "HUN", city = "Budapest" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Contains("country", problem!["detail"]!.GetValue<string>());
    }

    [Fact]
    public async Task Health_endpoints_respond()
    {
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/health/ready")).StatusCode);
    }
}

public class CatalogTests
{
    [Fact]
    public void Registered_sources_match_the_known_list_used_for_queue_setup()
    {
        var services = new ServiceCollection();
        services.AddScraping(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var catalog = services.BuildServiceProvider().GetRequiredService<SourceCatalog>();
        Assert.Equal(ScraperBusExtensions.KnownSources.Order(), catalog.All.Select(a => a.Name).Order());
    }

    [Fact]
    public void Catalog_picks_sources_by_country_and_leaves_demo_out()
    {
        var services = new ServiceCollection();
        services.AddScraping(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var catalog = services.BuildServiceProvider().GetRequiredService<SourceCatalog>();
        Assert.Equal(new[] { "alberlet", "ingatlan", "ingatlannet", "jofogas" }, catalog.For("HU", "Budapest").Select(a => a.Name).Order());
        Assert.DoesNotContain("ingatlan", catalog.For("HU", "Debrecen").Select(a => a.Name));
        Assert.Equal(new[] { "olx_ro", "storia" }, catalog.For("RO", "București").Select(a => a.Name).Order());
        Assert.Equal(new[] { "demo" }, catalog.For("PL", "Kraków").Select(a => a.Name));
        Assert.Equal(new[] { "demo" }, catalog.For("HU", "Budapest", ["demo"]).Select(a => a.Name));
    }
}
