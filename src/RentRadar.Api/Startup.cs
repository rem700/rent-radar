using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RentRadar.Api.Data;

namespace RentRadar.Api;

internal static class Startup
{
    public static bool UsePostgres(this IConfiguration config) =>
        string.Equals(config["Database:Provider"], "Postgres", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Creates the schema on first start (EnsureCreated). Retries for a while so the API can start
    /// together with its database in docker compose.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        if (!app.Configuration.UsePostgres())
        {
            var cs = new SqliteConnectionStringBuilder(app.Configuration.GetConnectionString("sqlite") ?? "Data Source=data/rent-radar.db");
            if (Path.GetDirectoryName(Path.GetFullPath(cs.DataSource)) is { } dir && cs.DataSource != ":memory:") Directory.CreateDirectory(dir);
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<RentRadarDb>();
                await db.Database.EnsureCreatedAsync();
                return;
            }
            catch (Exception ex) when (attempt < 10)
            {
                app.Logger.LogWarning(ex, "Database not ready (attempt {Attempt}), retrying", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}

/// <summary>Readiness of the cache: a round-trip through IDistributedCache (Redis when configured, memory otherwise).</summary>
internal sealed class DistributedCacheHealthCheck(IDistributedCache cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await cache.SetStringAsync("health", DateTime.UtcNow.ToString("O"), timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("cache unreachable", ex);
        }
    }
}
