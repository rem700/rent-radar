using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RentRadar.Api.Data;

namespace RentRadar.Api.Services;

public sealed class SchedulerOptions
{
    public const string Section = "Scheduler";
    /// <summary>Run exactly one scheduler per deployment (disable it on extra API replicas).</summary>
    public bool Enabled { get; set; } = true;
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan Tick { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Auto-update: every minute, restart the searches whose interval has passed; one full pass right after start-up.</summary>
public sealed class SchedulerService(IServiceScopeFactory scopes, IOptions<SchedulerOptions> options, ILogger<SchedulerService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return;
        try
        {
            await Task.Delay(options.Value.StartupDelay, ct);
            await TickAsync(force: true, ct);
            using var timer = new PeriodicTimer(options.Value.Tick);
            while (await timer.WaitForNextTickAsync(ct)) await TickAsync(force: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    internal async Task TickAsync(bool force, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RentRadarDb>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<RunDispatcher>();

            var closed = await dispatcher.CloseStaleRunsAsync(ct);
            if (closed > 0) logger.LogWarning("Closed {Count} source runs nobody answered", closed);

            var now = DateTime.UtcNow;
            var searches = await db.Searches.AsNoTracking().Where(s => s.AutoUpdate).ToListAsync(ct);
            foreach (var s in searches.Where(s => force || s.LastRunAt is null || now - s.LastRunAt >= TimeSpan.FromMinutes(s.IntervalMinutes)))
                await dispatcher.StartAsync(s.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Scheduler tick failed");
        }
    }
}
