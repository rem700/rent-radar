using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RentRadar.Domain;

/// <summary>Parsing helpers shared by all source adapters.</summary>
public static partial class TextParsing
{
    [GeneratedRegex(@"(?<=\d)[ .](?=\d{3}\b)")]
    private static partial Regex ThousandsSeparator();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?")]
    private static partial Regex Number();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlug();

    /// <summary>"450 000 Ft" → 450000, "62,5 m2" → 62.5, "1.800 lei" → 1800.</summary>
    public static double? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Replace(' ', ' ').Replace(' ', ' ');
        t = ThousandsSeparator().Replace(t, "");
        var m = Number().Match(t);
        if (!m.Success) return null;
        return double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }

    /// <summary>Removes diacritics and any other non-ASCII characters.</summary>
    public static string ToAscii(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Normalize(NormalizationForm.FormKD))
            if (ch < 128) sb.Append(ch);
        return sb.ToString();
    }

    /// <summary>"Győr" → "gyor", "Cluj-Napoca" → "cluj-napoca".</summary>
    public static string Slugify(string s) => NonSlug().Replace(ToAscii(s).ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex(
        @"(pets? (allowed|ok|friendly)|állat ?(barát|tartás engedélyezett|hozható)|kisállat (hozható|engedélyezett)|" +
        @"можна з тварин|з тваринами можна|можно с животн|с животными можно|tiere erlaubt|haustiere erlaubt|" +
        @"zvierat[áa] (s[úu] )?(povolen|v[íi]tan)|dom[áa]ce zvierat[áa] (s[úu] )?(povolen|v[íi]tan)|" +
        @"accept(ăm|am)? animale|animale(le)? (sunt )?acceptate|animale de companie acceptate)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PetsYes();

    [GeneratedRegex(
        @"(no pets|pets not allowed|állat nem|kisállat nem|без тварин|без домашніх тварин|без животн|" +
        @"животные не|тварини не|keine haustiere|keine tiere|" +
        @"bez zvierat|zvierat[áa] (nie s[úu] )?(nepovolen|nie)|" +
        @"f[ăa]r[ăa] animale|nu (se )?accept(ă|a|ăm|am) animale)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PetsNo();

    /// <summary>true = pets explicitly allowed, false = explicitly not allowed, null = not mentioned.</summary>
    public static bool? DetectPets(params string?[] texts)
    {
        var blob = string.Join(' ', texts.Where(t => !string.IsNullOrEmpty(t)));
        if (blob.Length == 0) return null;
        if (PetsNo().IsMatch(blob)) return false;
        if (PetsYes().IsMatch(blob)) return true;
        return null;
    }
}
