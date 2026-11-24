using System.Text.RegularExpressions;

namespace RentRadar.Domain;

/// <summary>The fields duplicate detection looks at.</summary>
public interface IDedupFacts
{
    string Source { get; }
    double? Rooms { get; }
    double? AreaM2 { get; }
    double? PriceEur { get; }
    double? Lat { get; }
    double? Lon { get; }
    bool GeoPrecise { get; }
    string? Address { get; }
}

/// <summary>
/// The same flat published on several sites becomes one property. Two listings are duplicates when
/// they come from different sources and match on rooms (if both have them), area ±3 m², price ±7 %
/// and place: closer than 300 m when both points are precise, otherwise the same street and no
/// further apart than 2.5 km.
/// </summary>
public static partial class Dedup
{
    public const double AreaTolerance = 3;
    public const double PriceTolerance = 0.07;

    [GeneratedRegex(@"\b(utca|ut|u|ter|street|st|vulytsia|vul|ul|ulitsa)\b\.?")]
    private static partial Regex StreetWords();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlnum();

    [GeneratedRegex(@"\d+[a-z/\-]*")]
    private static partial Regex HouseNumber();

    public static string NormalizeAddress(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var t = TextParsing.ToAscii(s).ToLowerInvariant();
        t = StreetWords().Replace(t, " ");
        return NonAlnum().Replace(t, " ").Trim();
    }

    /// <summary>Street name without the house number: "Dagály utca 5, XIII. kerület" → "dagaly".</summary>
    public static string StreetKey(string? s)
    {
        var first = (s ?? "").Split(',')[0];
        return HouseNumber().Replace(NormalizeAddress(first), " ").Trim();
    }

    public static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371000;
        double Rad(double d) => d * Math.PI / 180;
        var p1 = Rad(lat1);
        var p2 = Rad(lat2);
        var dp = p2 - p1;
        var dl = Rad(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dp / 2), 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Pow(Math.Sin(dl / 2), 2);
        return 2 * r * Math.Asin(Math.Sqrt(a));
    }

    public static bool IsDuplicate(IDedupFacts a, IDedupFacts b)
    {
        if (a.Source == b.Source) return false;
        if (a.Rooms is > 0 && b.Rooms is > 0 && Math.Abs(a.Rooms.Value - b.Rooms.Value) > 0.01) return false;
        if (a.AreaM2 is not > 0 || b.AreaM2 is not > 0 || Math.Abs(a.AreaM2.Value - b.AreaM2.Value) > AreaTolerance)
            return false;
        if (a.PriceEur is not > 0 || b.PriceEur is not > 0) return false;
        if (Math.Abs(a.PriceEur.Value - b.PriceEur.Value) / Math.Max(a.PriceEur.Value, b.PriceEur.Value) > PriceTolerance)
            return false;

        var bothPoints = a is { Lat: not null, Lon: not null } && b is { Lat: not null, Lon: not null };
        if (bothPoints)
        {
            var d = HaversineMeters(a.Lat!.Value, a.Lon!.Value, b.Lat!.Value, b.Lon!.Value);
            if (a.GeoPrecise && b.GeoPrecise) return d < 300;
            if (d > 2500) return false;
        }

        var sa = StreetKey(a.Address);
        return sa.Length > 0 && sa == StreetKey(b.Address);
    }
}
