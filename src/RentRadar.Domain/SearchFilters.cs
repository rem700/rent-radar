namespace RentRadar.Domain;

/// <summary>The listing fields the filters look at.</summary>
public interface IFilterableListing
{
    double? PriceEur { get; }
    double? Rooms { get; }
    double? AreaM2 { get; }
    string? District { get; }
    string? Address { get; }
    string Title { get; }
    string? Description { get; }
    bool? Pets { get; }
    bool? Furnished { get; }
}

/// <summary>Filters of a saved search. Prices are in EUR.</summary>
public sealed record SearchFilters
{
    public double? PriceMin { get; init; }
    public double? PriceMax { get; init; }
    public double? RoomsMin { get; init; }
    public double? RoomsMax { get; init; }
    public double? AreaMin { get; init; }
    public double? AreaMax { get; init; }
    /// <summary>Substring of the district or address.</summary>
    public string? District { get; init; }
    /// <summary>Comma-separated words that must all appear.</summary>
    public string? Keywords { get; init; }
    /// <summary>Comma-separated words none of which may appear.</summary>
    public string? Exclude { get; init; }
    /// <summary>true = only listings that allow pets; false = drop listings that forbid them.</summary>
    public bool? Pets { get; init; }
    public bool? Furnished { get; init; }

    public bool Matches(IFilterableListing l)
    {
        static bool InRange(double? v, double? lo, double? hi) =>
            (lo is null && hi is null) || (v is not null && (lo is null || v >= lo) && (hi is null || v <= hi));

        if (!InRange(l.PriceEur, PriceMin, PriceMax)) return false;
        if (!InRange(l.Rooms, RoomsMin, RoomsMax)) return false;
        if (!InRange(l.AreaM2, AreaMin, AreaMax)) return false;

        if (!string.IsNullOrWhiteSpace(District) &&
            !$"{l.District} {l.Address}".Contains(District.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        var text = $"{l.Title} {l.Description} {l.Address}";
        if (Words(Keywords).Any(w => !text.Contains(w, StringComparison.OrdinalIgnoreCase))) return false;
        if (Words(Exclude).Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase))) return false;

        if (Pets == true && l.Pets != true) return false;
        if (Pets == false && l.Pets == false) return false;
        if (Furnished is not null && l.Furnished is not null && l.Furnished != Furnished) return false;
        return true;
    }

    private static IEnumerable<string> Words(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
