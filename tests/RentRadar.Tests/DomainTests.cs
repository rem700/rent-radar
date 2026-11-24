using RentRadar.Domain;

namespace RentRadar.Tests;

public class DomainTests
{
    [Fact]
    public void ParseNumber_handles_separators_and_decimals()
    {
        Assert.Equal(450000, TextParsing.ParseNumber("450 000 Ft"));
        Assert.Equal(62.5, TextParsing.ParseNumber("62,5 m2"));
        Assert.Equal(1295, TextParsing.ParseNumber("1 295 USD"));
        Assert.Equal(1800, TextParsing.ParseNumber("1.800 lei"));
        Assert.Null(TextParsing.ParseNumber(null));
        Assert.Null(TextParsing.ParseNumber("ár megegyezés szerint"));
    }

    [Theory]
    [InlineData("Можна з тваринами", true)]
    [InlineData("Kisállat nem hozható", false)]
    [InlineData("nice flat", null)]
    [InlineData("Accept animale de companie", true)]
    [InlineData("Fara animale, nefumatori", false)]
    [InlineData("Domáce zvieratá vítané", true)]
    [InlineData("Byt je bez zvierat", false)]
    public void DetectPets_reads_hu_ro_sk_uk_phrases(string text, bool? expected) =>
        Assert.Equal(expected, TextParsing.DetectPets(text));

    [Theory]
    [InlineData("Győr", "gyor")]
    [InlineData("București", "bucuresti")]
    [InlineData("Cluj-Napoca", "cluj-napoca")]
    [InlineData("Košice", "kosice")]
    public void Slugify_strips_diacritics(string input, string expected) => Assert.Equal(expected, TextParsing.Slugify(input));

    [Fact]
    public void StreetKey_drops_house_number_and_street_words()
    {
        Assert.Equal("dagaly", Dedup.StreetKey("Dagály utca 5, XIII. kerület"));
        Assert.Equal(Dedup.StreetKey("X utca 5"), Dedup.StreetKey("X utca 7, Budapest"));
    }

    private sealed record Facts(
        string Source = "a", double? Rooms = 2, double? AreaM2 = 50, double? PriceEur = 800,
        double? Lat = 47.5, double? Lon = 19.05, bool GeoPrecise = true, string? Address = "X utca 5") : IDedupFacts;

    [Fact]
    public void IsDuplicate_follows_the_matching_rules()
    {
        var a = new Facts();
        Assert.True(Dedup.IsDuplicate(a, new Facts(Source: "b", AreaM2: 52, PriceEur: 830, Lat: 47.501)));
        Assert.False(Dedup.IsDuplicate(a, new Facts(Source: "a"))); // same source
        Assert.False(Dedup.IsDuplicate(a, new Facts(Source: "b", Rooms: 3)));
        Assert.False(Dedup.IsDuplicate(a, new Facts(Source: "b", PriceEur: 1000)));
        Assert.False(Dedup.IsDuplicate(a, new Facts(Source: "b", Lat: 47.52))); // > 2 km
        Assert.True(Dedup.IsDuplicate(new Facts(GeoPrecise: false), new Facts(Source: "b", GeoPrecise: false, Address: "X utca 7, Budapest")));
        Assert.False(Dedup.IsDuplicate(new Facts(GeoPrecise: false), new Facts(Source: "b", GeoPrecise: false, Address: "Y utca 5")));
    }

    private sealed record Flat(
        double? PriceEur = 700, double? Rooms = 2, double? AreaM2 = 55, string? District = "XIII. kerület",
        string? Address = "Dagály utca, Budapest", string Title = "Bright flat with balcony", string? Description = "Pets allowed",
        bool? Pets = true, bool? Furnished = null) : IFilterableListing;

    [Fact]
    public void Filters_match_ranges_words_and_flags()
    {
        var flat = new Flat();
        Assert.True(new SearchFilters { PriceMax = 800, RoomsMin = 2, District = "xiii" }.Matches(flat));
        Assert.False(new SearchFilters { PriceMax = 600 }.Matches(flat));
        Assert.True(new SearchFilters { Keywords = "balcony, bright" }.Matches(flat));
        Assert.False(new SearchFilters { Exclude = "balcony" }.Matches(flat));
        Assert.True(new SearchFilters { Pets = true, Furnished = true }.Matches(flat)); // unknown furniture is not excluded
        Assert.False(new SearchFilters { AreaMin = 10 }.Matches(flat with { AreaM2 = null }));
    }

    [Fact]
    public void ToEur_uses_rates_and_rounds()
    {
        Assert.Equal(1000, Currencies.ToEur(395000, "HUF", Currencies.Fallback));
        Assert.Equal(100, Currencies.ToEur(500, "ron", Currencies.Fallback));
        Assert.Null(Currencies.ToEur(10, "XYZ", Currencies.Fallback));
        Assert.Null(Currencies.ToEur(null, "EUR", Currencies.Fallback));
    }
}
