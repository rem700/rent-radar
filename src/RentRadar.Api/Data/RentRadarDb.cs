using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RentRadar.Domain;

namespace RentRadar.Api.Data;

/// <summary>
/// One model for PostgreSQL (docker compose) and SQLite (single-process mode). Small nested values —
/// filters, image lists, price history — are stored as JSON text so both providers behave the same.
/// </summary>
public sealed class RentRadarDb(DbContextOptions<RentRadarDb> options) : DbContext(options)
{
    public DbSet<Search> Searches => Set<Search>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<SourceRun> SourceRuns => Set<SourceRun>();
    public DbSet<GeoCacheEntry> GeoCache => Set<GeoCacheEntry>();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // every DateTime is UTC: written as UTC, read back with Kind=Utc (SQLite loses the kind)
        builder.Properties<DateTime>().HaveConversion<UtcConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Search>(e =>
        {
            e.ToTable("searches");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Country).HasMaxLength(2);
            e.Property(x => x.City).HasMaxLength(200);
            e.Property(x => x.Filters).HasConversion(Json<SearchFilters>(() => new SearchFilters()));
            e.Property(x => x.Sources).HasConversion(NullableJson<List<string>>(), ListComparer<string>());
        });

        b.Entity<Property>(e =>
        {
            e.ToTable("properties");
            e.Ignore(x => x.HasUserState);
        });

        b.Entity<Listing>(e =>
        {
            e.ToTable("listings");
            e.HasIndex(x => new { x.SearchId, x.Source, x.ExternalId }).IsUnique();
            e.HasIndex(x => x.PropertyId);
            e.HasIndex(x => x.FirstSeen);
            e.HasOne<Search>().WithMany().HasForeignKey(x => x.SearchId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Property).WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Source).HasMaxLength(50);
            e.Property(x => x.ExternalId).HasMaxLength(100);
            e.Property(x => x.Url).HasMaxLength(1000);
            e.Property(x => x.Title).HasMaxLength(500);
            e.Property(x => x.Currency).HasMaxLength(10);
            e.Property(x => x.Floor).HasMaxLength(50);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.District).HasMaxLength(200);
            e.Property(x => x.Images).HasConversion(Json<List<string>>(() => []), ListComparer<string>());
            e.Property(x => x.PriceHistory).HasConversion(Json<List<PricePoint>>(() => []), ListComparer<PricePoint>());
        });

        b.Entity<SourceRun>(e =>
        {
            e.ToTable("source_runs");
            e.HasIndex(x => new { x.SearchId, x.StartedAt });
            e.HasOne<Search>().WithMany().HasForeignKey(x => x.SearchId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Source).HasMaxLength(50);
        });

        b.Entity<GeoCacheEntry>(e =>
        {
            e.ToTable("geocache");
            e.HasKey(x => x.Query);
            e.Property(x => x.Query).HasMaxLength(500);
        });
    }

    private static ValueConverter<T, string> Json<T>(Func<T> empty) where T : class =>
        new(v => JsonSerializer.Serialize(v, JsonOptions),
            v => JsonSerializer.Deserialize<T>(v, JsonOptions) ?? empty());

    private static ValueConverter<T?, string?> NullableJson<T>() where T : class =>
        new(v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
            v => v == null ? null : JsonSerializer.Deserialize<T>(v, JsonOptions));

    private static ValueComparer<List<T>?> ListComparer<T>() =>
        new((a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
            v => v == null ? 0 : v.Aggregate(0, (h, x) => HashCode.Combine(h, x)),
            v => v == null ? null : v.ToList());

    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v.ToUniversalTime(), DateTimeKind.Utc),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
