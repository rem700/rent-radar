namespace RentRadar.Domain;

public static class Currencies
{
    /// <summary>Units per 1 EUR, used when the live rates are unavailable (approximate).</summary>
    public static readonly IReadOnlyDictionary<string, double> Fallback = new Dictionary<string, double>
    {
        ["EUR"] = 1.0, ["USD"] = 1.08, ["HUF"] = 395.0, ["RON"] = 5.0, ["CZK"] = 25.0, ["PLN"] = 4.3,
        ["BGN"] = 1.96, ["GBP"] = 0.85, ["CHF"] = 0.95, ["UAH"] = 45.0,
    };

    public static double? ToEur(double? amount, string? currency, IReadOnlyDictionary<string, double> rates)
    {
        if (amount is null) return null;
        return rates.TryGetValue((currency ?? "EUR").ToUpperInvariant(), out var rate) && rate > 0
            ? Math.Round(amount.Value / rate, 2)
            : null;
    }
}
