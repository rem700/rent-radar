using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace RentRadar.Scraping;

/// <summary>Small helpers on top of AngleSharp and System.Text.Json used by the adapters.</summary>
internal static partial class Html
{
    private static readonly HtmlParser Parser = new();

    public static IHtmlDocument Parse(string html) => Parser.ParseDocument(html);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>Text of all descendant text nodes, each trimmed, joined with a space (like BeautifulSoup's get_text(" ", strip=True)).</summary>
    public static string Text(INode? node, bool skipStyle = false)
    {
        if (node is null) return "";
        var parts = node.GetDescendants()
            .OfType<IText>()
            .Where(t => !skipStyle || t.ParentElement?.LocalName is not ("style" or "script"))
            .Select(t => t.Data.Trim())
            .Where(t => t.Length > 0);
        return string.Join(' ', parts);
    }

    public static string Squash(string s) => Spaces().Replace(s, " ").Trim();

    public static string Absolute(string baseUrl, string href) => new Uri(new Uri(baseUrl), href).AbsoluteUri;

    /// <summary>JSON from &lt;script id="__NEXT_DATA__"&gt; of a Next.js page.</summary>
    public static JsonNode? NextData(string html, string site)
    {
        var script = Parse(html).GetElementById("__NEXT_DATA__");
        if (script is null || string.IsNullOrWhiteSpace(script.TextContent))
            throw new AdapterException($"{site}: the page has no __NEXT_DATA__, the site layout has changed");
        return JsonNode.Parse(script.TextContent);
    }
}

/// <summary>Null-safe navigation over JsonNode, tolerant to numbers stored as strings and vice versa.</summary>
internal static class Json
{
    public static JsonNode? P(this JsonNode? n, string key) =>
        n is JsonObject o && o.TryGetPropertyValue(key, out var v) ? v : null;

    public static JsonNode? P(this JsonNode? n, params string[] path)
    {
        foreach (var key in path) n = n.P(key);
        return n;
    }

    public static IEnumerable<JsonNode> Items(this JsonNode? n) =>
        n is JsonArray a ? a.Where(x => x is not null).Select(x => x!) : [];

    public static string? Str(this JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<string>(out var s)) return s;
        if (v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        if (v.TryGetValue<double>(out var d)) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return v.ToJsonString();
    }

    public static double? Num(this JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<string>(out var s) &&
            double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p))
            return p;
        return null;
    }

    public static bool? Bool(this JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    /// <summary>Python-style truthiness for "x or y" chains.</summary>
    public static bool Truthy(this JsonNode? n) => n switch
    {
        null => false,
        JsonArray a => a.Count > 0,
        JsonObject o => o.Count > 0,
        JsonValue v when v.TryGetValue<string>(out var s) => s.Length > 0,
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<double>(out var d) => d != 0,
        _ => false,
    };

    public static DateTime? Date(this JsonNode? n)
    {
        var s = n.Str();
        if (string.IsNullOrEmpty(s)) return null;
        if (DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var dto))
            return dto.UtcDateTime;
        if (s.Length >= 19 && DateTime.TryParse(s[..19], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
            return dt;
        return null;
    }
}
