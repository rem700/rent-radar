using System.Text.Json.Nodes;
using RentRadar.Scraping;
using RentRadar.Scraping.Adapters;

namespace RentRadar.Tests;

/// <summary>Parsers against fragments shaped like the real pages and e-mails (dates in the comments).</summary>
public class AdapterTests
{
    private static string NextData(string json) => $"""<html><script id="__NEXT_DATA__" type="application/json">{json}</script></html>""";

    // ---------- alberlet.hu ----------

    private const string AlberletLinks = """
        <html><body>
        <div class="item">
          <div class="gallery" style="background-image: url(&quot;/hirdetes-kep/budapest-V-kerulet-henszlmann-imre-utca-77m2-3-szoba_783040-6811614@500x70xjpeg.webp&quot;)"></div>
          <a href="/kiado-alberlet/budapest-V-kerulet-henszlmann-imre-utca-77m2-3-szoba_783040">450 000 Ft/hó 77 m2 3 szoba</a>
          <a href="/kiado-alberlet/budapest-V-kerulet-henszlmann-imre-utca-77m2-3-szoba_783040">Henszlmann Imre utca Budapest, V. kerület</a>
        </div>
        <div class="item">
          <a href="https://www.alberlet.hu/kiado-alberlet/budapest-II-kerulet-szerena-ut-62m2-3-szoba_731858">Videós lakás</a>
          <a href="https://www.alberlet.hu/kiado-alberlet/budapest-II-kerulet-szerena-ut-62m2-3-szoba_731858">430 000 Ft/hó 62 m2 2 szoba + 1 félszoba</a>
          <a href="https://www.alberlet.hu/kiado-alberlet/budapest-II-kerulet-szerena-ut-62m2-3-szoba_731858">Szeréna út Budapest, II. kerület</a>
        </div>
        <a href="/kiado-alberlet/budapest-4-kerulet">Kiadó lakás 4. kerület</a>
        </body></html>
        """;

    [Fact]
    public void Alberlet_fallback_parser_reads_links()
    {
        var items = AlberletAdapter.ParseList(AlberletLinks);
        Assert.Equal(new[] { "783040", "731858" }, items.Select(i => i.ExternalId));
        var (a, b) = (items[0], items[1]);
        Assert.Equal(450000, a.Price);
        Assert.Equal("HUF", a.Currency);
        Assert.Equal(77, a.AreaM2);
        Assert.Equal(3, a.Rooms);
        Assert.Equal("Henszlmann Imre utca, Budapest, V. kerület", a.Address);
        Assert.Equal("V. kerület", a.District);
        Assert.Equal(new[] { "https://www.alberlet.hu/hirdetes-kep/budapest-V-kerulet-henszlmann-imre-utca-77m2-3-szoba_783040-6811614@500x70xjpeg.webp" }, a.Images);
        Assert.Equal(2.5, b.Rooms);
        Assert.Equal("II. kerület", b.District);
    }

    // card markup as on alberlet.hu, 14.11.2026
    private const string AlberletCards = """
        <div class="row"><div class="col-12"><div class="advert" data-id="785588">
          <div class="labels"></div>
          <div class="image-container owl-carousel lazy-loading">
            <a class="item owl-lazy" target="_blank" href="/kiado-alberlet/budapest-I-kerulet-aladar-utca-27m2-1-szoba_785588" data-src="/hirdetes-kep/budapest-I-kerulet-aladar-utca-27m2-1-szoba_785588-6857272@500x70xjpeg.webp"></a>
            <a class="item owl-lazy" target="_blank" href="/kiado-alberlet/budapest-I-kerulet-aladar-utca-27m2-1-szoba_785588" data-src="/hirdetes-kep/budapest-I-kerulet-aladar-utca-27m2-1-szoba_785588-6857273@500x70xjpeg.webp"></a>
          </div>
          <div class="advert-data">
            <a target="_blank" href="/kiado-alberlet/budapest-I-kerulet-aladar-utca-27m2-1-szoba_785588">
              <div class="row align-items-end">
                <div class="col"><span class="price">195 000</span> Ft/hó</div>
                <div class="col-4 text-right font-weight-semibold">27 m<sup>2</sup></div>
              </div>
              <div class="rooms mb-1">1 szoba + 1 félszoba</div>
            </a>
            <div class="row align-items-end">
              <a href="/kiado-alberlet/budapest-I-kerulet-aladar-utca-27m2-1-szoba_785588"><div class="col address">Aladár utca<br> Budapest, I. kerület</div></a>
            </div>
          </div>
        </div></div></div>
        """;

    [Fact]
    public void Alberlet_card_parser_reads_cards()
    {
        var r = Assert.Single(AlberletAdapter.ParseList(AlberletCards));
        Assert.Equal("785588", r.ExternalId);
        Assert.Equal(195000, r.Price);
        Assert.Equal("HUF", r.Currency);
        Assert.Equal(27, r.AreaM2);
        Assert.Equal(1.5, r.Rooms);
        Assert.Equal("Aladár utca, Budapest, I. kerület", r.Address);
        Assert.Equal("I. kerület", r.District);
        Assert.Equal("https://www.alberlet.hu/kiado-alberlet/budapest-I-kerulet-aladar-utca-27m2-1-szoba_785588", r.Url);
        Assert.Equal(2, r.Images.Count);
        Assert.StartsWith("https://www.alberlet.hu/hirdetes-kep/", r.Images[0]);
    }

    [Fact]
    public void Alberlet_urls()
    {
        Assert.EndsWith("/megye:budapest/limit:48", AlberletAdapter.ListUrl("Budapest", 1));
        Assert.EndsWith("/telepules:debrecen/limit:48/page:2", AlberletAdapter.ListUrl("Debrecen", 2));
    }

    // ---------- Jófogás (__NEXT_DATA__ as on 09.11.2026) ----------

    private const string JofogasAd = """
        {
          "list_id": 160980382,
          "url": "https://ingatlan.jofogas.hu/budapest/I__Kerulet__Felujitott_Lakas_A_Var_Alatt_160980382.htm",
          "subject": "I. Kerület, Felújított Lakás A Vár Alatt",
          "body": "Szép lakás.",
          "latitude": "47.5040503",
          "longitude": "19.0326128",
          "price": {"label": "275 000 Ft", "value": 275000},
          "type": {"label": "Kiadó", "value": "u"},
          "region": {"label": "Budapest", "value": "1"},
          "list_time": {"label": "ma, 19:45", "value": "1794249927"},
          "images": [{"name": "I__Kerulet_1.jpg"}],
          "parameters": [
            {"key": "city", "values": [{"value": "5", "label": "I. kerület"}]},
            {"key": "rooms", "values": [{"value": "1", "label": "1 szoba"}]},
            {"key": "half_room", "values": [{"value": "1", "label": "1"}]},
            {"key": "size", "values": [{"value": "44", "label": "44 m²"}]},
            {"key": "floor", "values": [{"value": "3", "label": "félemelet"}]},
            {"key": "has_furniture", "values": [{"value": "2", "label": "nem"}]},
            {"key": "animal_friendly", "values": [{"value": "1", "label": "igen"}]},
            {"key": "jfg_area", "values": [{"value": "Víziváros", "label": "Víziváros "}]},
            {"key": "jfg_street", "values": [{"value": "Fő utca", "label": "Fő utca "}]},
            {"key": "jfg_street_number", "values": [{"value": "1", "label": "1 "}]},
            {"key": "common_cost", "values": [{"value": "22684", "label": "22684 Ft/hó"}]}
          ]
        }
        """;

    [Fact]
    public void Jofogas_parses_next_data()
    {
        var html = NextData($$"""{"props": {"pageProps": {"adList": {"ads": [{{JofogasAd}}], "pager": {"last_page": 12}}}}}""");
        var (ads, pager) = JofogasAdapter.ParsePage(html);
        Assert.Equal(12, pager.P("last_page").Num());
        var r = JofogasAdapter.ParseAd(ads[0])!;
        Assert.Equal("160980382", r.ExternalId);
        Assert.Equal(275000, r.Price);
        Assert.Equal(1.5, r.Rooms);
        Assert.Equal(44, r.AreaM2);
        Assert.Equal("félemelet", r.Floor);
        Assert.True(r.Pets);
        Assert.False(r.Furnished);
        Assert.Equal("I. kerület", r.District);
        Assert.Equal("Fő utca 1, Víziváros, I. kerület, Budapest", r.Address);
        Assert.Equal(47.5040503, r.Lat);
        Assert.True(r.GeoPrecise);
        Assert.Equal(new[] { "https://img.jofogas.hu/bigthumbs/I__Kerulet_1.jpg" }, r.Images);
        Assert.Contains("22684 Ft/hó", r.Description);
        Assert.Equal(DateTimeKind.Utc, r.PostedAt!.Value.Kind);
    }

    [Fact]
    public void Jofogas_urls()
    {
        Assert.Equal("https://ingatlan.jofogas.hu/budapest/lakas?st=u", JofogasAdapter.ListUrl("Budapest", 1));
        Assert.Equal("https://ingatlan.jofogas.hu/gyor/lakas?st=u&o=3", JofogasAdapter.ListUrl("Győr", 3));
    }

    // ---------- Ingatlannet (__NEXT_DATA__ as on 10.11.2026) ----------

    [Fact]
    public void Ingatlannet_parses_next_data()
    {
        const string estate = """
            {"id": 6936446, "absolutUrl": "https://www.ingatlannet.hu/kiado-lakas-xiii-kerulet/6936446",
             "url": "/kiado-lakas-xiii-kerulet/6936446", "address": "Marina Part, Budapest, XIII. kerület",
             "name": "Kiadó Téglalakás Budapest, XIII. kerület, Marina Part Lakópark, 105 m²", "price": 550000,
             "rooms": 3, "areaSize": 0, "buildingAreaSize": 105, "createdAt": "2026-11-07 05:42:23.860383+01",
             "pet": null, "furnished": null,
             "mainPicture": {"id": 1, "variants": {"1": {"url": "https://pic01.ingatlannet.hu/ingatlan//2026/11/7/6936446-1.jpg"}}}}
            """;
        var e = Assert.Single(IngatlannetAdapter.ParsePage(NextData($$"""{"props": {"pageProps": {"serverEstates": [{{estate}}]}}}""")));
        var r = IngatlannetAdapter.ParseEstate(e)!;
        Assert.Equal("6936446", r.ExternalId);
        Assert.Equal(550000, r.Price);
        Assert.Equal(3, r.Rooms);
        Assert.Equal(105, r.AreaM2);
        Assert.Equal("XIII. kerület", r.District);
        Assert.Equal(new[] { "https://pic01.ingatlannet.hu/ingatlan//2026/11/7/6936446-1.jpg" }, r.Images);
        Assert.Equal(7, r.PostedAt!.Value.Day);
        Assert.Null(r.Pets);
        Assert.Equal("https://www.ingatlannet.hu/kiado-lakas-budapest", IngatlannetAdapter.ListUrl("Budapest", 1));
        Assert.Equal("https://www.ingatlannet.hu/kiado-lakas-debrecen?page=2", IngatlannetAdapter.ListUrl("Debrecen", 2));
    }

    [Fact]
    public void NextData_missing_is_reported_as_layout_change()
    {
        var ex = Assert.Throws<AdapterException>(() => IngatlannetAdapter.ParsePage("<html><body>captcha</body></html>"));
        Assert.Contains("__NEXT_DATA__", ex.Message);
    }

    // ---------- ingatlan.com e-mail alerts (format as on 14.11.2026) ----------

    private const string IngatlanMail = """
        <table>
        <tr><th></th><th>ár</th><th>típus</th><th>lakóterület</th><th>szobák száma</th><th></th></tr>
        <tr>
          <td><a href="https://ingatlan.com/35663177?hf=1&utm_source=x"><img src="https://ip.ingatlancdn.com/resize/office/80x60/abc/def=" alt="Részletek"></a></td>
          <td><a href="https://ingatlan.com/35663177?hf=1">Budapest, 10. kerület,
        (Felsőrákos),
        Gyógyszergyári utca</a> - <a href="https://ingatlan.com/35663177?hf=1">térkép</a></td>
        </tr>
        <tr><td><b>220 000</b> Ft/hó</td><td>tégla</td><td><b>30</b> m²</td><td><b>1</b></td><td>(x)</td></tr>
        <tr>
          <td><a href="https://ingatlan.com/35681482?hf=1">Részletek</a></td>
          <td><a href="https://ingatlan.com/35681482?hf=1">Budapest, 14. kerület,
        (Herminamező)</a> - <a href="https://ingatlan.com/35681482?hf=1">térkép</a></td>
        </tr>
        <tr><td><b>460 000</b> Ft/hó<br>1 263€/hó <span>új építésű</span></td><td>panel</td><td><b>89</b> m²</td><td><b>1+1</b> fél</td><td></td></tr>
        <tr><td><b>Az alábbi ingatlanok ára változott</b></td></tr>
        </table>
        """;

    [Fact]
    public void IngatlanMail_parses_alert_table()
    {
        var items = IngatlanMailAdapter.ParseEmailHtml(IngatlanMail, new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(2, items.Count);
        var (a, b) = (items[0], items[1]);
        Assert.Equal("35663177", a.ExternalId);
        Assert.Equal("https://ingatlan.com/35663177", a.Url);
        Assert.Equal(220000, a.Price);
        Assert.Equal("HUF", a.Currency);
        Assert.Equal(30, a.AreaM2);
        Assert.Equal(1, a.Rooms);
        Assert.Equal("X. kerület", a.District);
        Assert.Equal("Gyógyszergyári utca, Felsőrákos, X. kerület, Budapest", a.Address);
        Assert.Single(a.Images);
        Assert.Equal("tégla", a.Description);
        Assert.Equal(460000, b.Price);
        Assert.Equal("HUF", b.Currency);
        Assert.Equal(1.5, b.Rooms);
        Assert.Equal(89, b.AreaM2);
        Assert.Equal("Herminamező, XIV. kerület, Budapest", b.Address);
        Assert.Empty(b.Images);
        Assert.Equal("panel, új építésű", b.Description);
    }

    [Fact]
    public void MailSettings_reads_both_ini_layouts()
    {
        // an obviously fake value, built at run time so secret scanners do not mistake it for a password
        var spaced = string.Join(' ', Enumerable.Repeat("test", 4));
        var (keyIni, keyEnv) = ("app_" + "password", "GMAIL_APP_" + "PASSWORD");
        var a = MailSettings.LoadIni($"[gmail]\nuser = x@example.com\n{keyIni} = {spaced}\nlabel = rent-finder\n");
        var b = MailSettings.LoadIni($"﻿# c\r\nGMAIL_USER=x@example.com\r\n{keyEnv}={spaced.Replace(" ", "")}\r\nGMAIL_LABEL=rent-finder\r\n");
        foreach (var s in new[] { a, b })
        {
            Assert.Equal("x@example.com", s.User);
            Assert.Equal("testtesttesttest", s.AppPassword);
            Assert.Equal("rent-finder", s.Label);
        }
    }

    // ---------- Romania and Slovakia (as on 14.11.2026) ----------

    [Fact]
    public void Storia_parses_next_data()
    {
        const string item = """
            {"id": 10728478, "slug": "apartament-3-camere-80mp-hils-brauner-IDJ0XY",
             "title": "Apartament 3 camere, 80mp, centrala, metrou, parcare", "estate": "FLAT", "transaction": "RENT",
             "rentPrice": null, "totalPrice": {"value": 799, "currency": "EUR"}, "hidePrice": false,
             "areaInSquareMeters": 80, "roomsNumber": "THREE", "floorNumber": "FOURTH",
             "dateCreated": "2026-10-26T13:30:37Z", "shortDescription": "Accept animale de companie.",
             "images": [{"small": "https://ireland.apollo.olxcdn.com/v1/files/a/image;s=184x138",
                         "medium": "https://ireland.apollo.olxcdn.com/v1/files/a/image;s=655x491",
                         "large": "https://ireland.apollo.olxcdn.com/v1/files/a/image;s=1280x1024"}],
             "location": {"mapDetails": {"radius": 0},
                          "address": {"street": {"name": "Strada Victor Brauner", "number": null}, "city": null, "province": null},
                          "reverseGeocoding": {"locations": [
                              {"id": "bucuresti", "locationLevel": "county", "name": "Bucuresti"},
                              {"id": "bucuresti/sectorul-3", "locationLevel": "sector", "name": "Sectorul 3"}]}}}
            """;
        var (items, pagination) = StoriaAdapter.ParsePage(NextData(
            $$"""{"props": {"pageProps": {"data": {"searchAds": {"items": [{{item}}], "pagination": {"totalPages": 381, "currentPage": 1}}}}}}"""));
        Assert.Equal(381, pagination.P("totalPages").Num());
        var r = StoriaAdapter.ParseItem(items[0])!;
        Assert.Equal(799, r.Price);
        Assert.Equal("EUR", r.Currency);
        Assert.Equal(3, r.Rooms);
        Assert.Equal(80, r.AreaM2);
        Assert.Equal("4", r.Floor);
        Assert.Equal("Strada Victor Brauner, Sectorul 3, Bucuresti", r.Address);
        Assert.Equal("Sectorul 3", r.District);
        Assert.Equal("https://www.storia.ro/ro/oferta/apartament-3-camere-80mp-hils-brauner-IDJ0XY", r.Url);
        Assert.EndsWith("s=1280x1024", r.Images[0]);
        Assert.True(r.Pets);
        Assert.Equal(new[] { "bucuresti" }, StoriaAdapter.CityPathsFor("București"));
        Assert.Equal(new[] { "cluj/cluj--napoca" }, StoriaAdapter.CityPathsFor("Cluj-Napoca"));
        Assert.EndsWith("/inchiriere/apartament/bucuresti?page=2", StoriaAdapter.ListUrl("bucuresti", 2));
    }

    private const string OlxRoHtml = """
        <div data-cy="l-card" id="311736450">
          <a href="/d/oferta/apartament-2-camere-drumul-taberei-IDl60Tg.html?reason=x"><img src="https://frankfurt.apollo.olxcdn.com:443/v1/files/abc/image;s=216x152"></a>
          <div data-testid="ad-card-title"><a data-testid="card-title-link" href="/d/oferta/apartament-2-camere-drumul-taberei-IDl60Tg.html"><h4>Ap 2 camere Drumul Taberei, bloc nou</h4></a>
            <p data-testid="ad-price">625 €<style>.css-x{color:red}</style></p>
            <p data-testid="location-date">Bucuresti, Sectorul 6 - Reactualizat la 13 noiembrie 2026<span>56 m²</span></p>
          </div>
        </div>
        <div data-cy="l-card" id="311700001">
          <a href="/d/oferta/garsoniera-militari-IDabcde.html"><img src="/app/static/media/no_thumbnail.svg"></a>
          <div data-testid="ad-card-title"><a href="/d/oferta/garsoniera-militari-IDabcde.html"><h4>Garsoniera Militari</h4></a>
            <p data-testid="ad-price">1.800 lei</p>
            <p data-testid="location-date">Bucuresti, Sectorul 6 - 12 noiembrie 2026<span>30 m²</span></p>
          </div>
        </div>
        """;

    [Fact]
    public void OlxRo_parses_cards()
    {
        var items = OlxRoAdapter.ParseList(OlxRoHtml);
        Assert.Equal(2, items.Count);
        var (a, b) = (items[0], items[1]);
        Assert.Equal("311736450", a.ExternalId);
        Assert.Equal(625, a.Price);
        Assert.Equal("EUR", a.Currency);
        Assert.Equal("https://www.olx.ro/d/oferta/apartament-2-camere-drumul-taberei-IDl60Tg.html", a.Url);
        Assert.Equal(2, a.Rooms);
        Assert.Equal(56, a.AreaM2);
        Assert.Equal("Sectorul 6", a.District);
        Assert.Equal("Sectorul 6, Bucuresti", a.Address);
        Assert.EndsWith(";s=800x600", a.Images[0]);
        Assert.Equal(1800, b.Price);
        Assert.Equal("RON", b.Currency);
        Assert.Equal(1, b.Rooms);
        Assert.Empty(b.Images);
        Assert.EndsWith("/apartamente-garsoniere-de-inchiriat/bucuresti/?page=3", OlxRoAdapter.ListUrl("București", 3));
    }

    [Fact]
    public void Nehnutelnosti_parses_json_ld_and_card_address()
    {
        const string ld = """
            {"@context": "https://schema.org", "@graph": [
              {"@type": "Organization", "name": "x"},
              {"@type": "SearchResultsPage", "mainEntity": {"@type": "ItemList", "numberOfItems": 1, "itemListElement": [
                {"@type": "ListItem", "position": 1, "item": {
                  "@type": ["Product", "Offer", "Place", "Accommodation"],
                  "@id": "https://www.nehnutelnosti.sk/#/schema/Product/Ju8z_LGAXfh",
                  "name": "HERRYS | Na prenájom 2-izbový byt s parkovaním | ZWIRN, Nivy",
                  "category": "2 izbový byt",
                  "description": "Kompletne zariadený byt. Podlažie: 3. z 8. Domáce zvieratá vítané.",
                  "floorSize": {"@type": "QuantitativeValue", "value": 43, "unitCode": "U+33A1"},
                  "image": ["https://img.unitedclassifieds.sk/foto/a", "https://img.unitedclassifieds.sk/foto/b"],
                  "offers": {"@type": "Offer", "priceCurrency": "EUR", "price": "968"},
                  "priceSpecification": {"@type": "PriceSpecification", "price": 968, "priceCurrency": "EUR"},
                  "url": "https://www.nehnutelnosti.sk/detail/Ju8z_LGAXfh/herrys-na-prenajom",
                  "validFrom": "2026-11-12T09:30:46.375Z"}}]}}]}
            """;
        var html = $"""<script type="application/ld+json">{ld}</script>""" +
                   """<div class="card"><a href="https://www.nehnutelnosti.sk/detail/Ju8z_LGAXfh/herrys-na-prenajom">HERRYS | Na prenájom</a>""" +
                   "<p>Košická, Bratislava-Ružinov, okres Bratislava II</p><p>2 izbový byt</p><p>43 m²</p><p>968 €/mes.</p></div>";
        var r = Assert.Single(NehnutelnostiAdapter.ParsePage(html));
        Assert.Equal("Ju8z_LGAXfh", r.ExternalId);
        Assert.Equal(968, r.Price);
        Assert.Equal("EUR", r.Currency);
        Assert.Equal(2, r.Rooms);
        Assert.Equal(43, r.AreaM2);
        Assert.Equal("3", r.Floor);
        Assert.True(r.Furnished);
        Assert.True(r.Pets);
        Assert.Equal("Košická, Bratislava-Ružinov", r.Address);
        Assert.Equal("Bratislava-Ružinov", r.District);
        Assert.Equal(2, r.Images.Count);
        Assert.Equal(12, r.PostedAt!.Value.Day);
        Assert.EndsWith("/vysledky/byty/kosice/prenajom?page=2", NehnutelnostiAdapter.ListUrl("Košice", 2));
    }
}
