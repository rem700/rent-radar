namespace RentRadar.Scraping;

/// <summary>All known sources and which of them serve a given country and city.</summary>
public sealed class SourceCatalog(IEnumerable<ISourceAdapter> adapters)
{
    private readonly Dictionary<string, ISourceAdapter> _byName = adapters.ToDictionary(a => a.Name);

    public IReadOnlyCollection<ISourceAdapter> All => _byName.Values;

    public ISourceAdapter? Get(string name) => _byName.GetValueOrDefault(name);

    /// <summary>
    /// Sources for a search. <paramref name="only"/> = null means every matching source; the demo source is
    /// then left out whenever real ones exist.
    /// </summary>
    public IReadOnlyList<ISourceAdapter> For(string country, string city, IReadOnlyCollection<string>? only = null)
    {
        var matching = _byName.Values.Where(a => a.Supports(country, city)).ToList();
        if (only is not null) return matching.Where(a => only.Contains(a.Name)).ToList();
        return matching.Any(a => a.Name != "demo") ? matching.Where(a => a.Name != "demo").ToList() : matching;
    }

    /// <summary>Country code → names of the sources that cover it ("*" sources are not listed).</summary>
    public IReadOnlyDictionary<string, List<string>> SupportedCountries()
    {
        var result = new SortedDictionary<string, List<string>>();
        foreach (var a in _byName.Values)
        foreach (var c in a.Countries.Where(c => c != "*"))
        {
            if (!result.TryGetValue(c, out var list)) result[c] = list = [];
            list.Add(a.Name);
        }
        return result;
    }
}
