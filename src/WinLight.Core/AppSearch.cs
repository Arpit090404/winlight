namespace WinLight.Core;

public sealed class AppSearch
{
    private AppItem[] _items = [];
    private readonly Dictionary<string, UsageRecord> _usage = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _items.Length;

    public void SetItems(IEnumerable<AppItem> items)
    {
        _items = AppDeduplicator.Deduplicate(items);
        Array.Sort(_items, static (a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
    }

    public void RecordLaunch(AppItem item, DateTimeOffset now)
    {
        _usage.TryGetValue(item.Identity, out UsageRecord previous);
        _usage[item.Identity] = new UsageRecord(previous.Count + 1, now);
    }

    public void LoadUsage(IReadOnlyDictionary<string, UsageRecord> usage)
    {
        _usage.Clear();
        foreach (var pair in usage) _usage[pair.Key] = pair.Value;
    }

    public Dictionary<string, UsageRecord> SnapshotUsage() => new(_usage, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AppItem> Find(string query, int limit = 8)
    {
        IReadOnlyList<AppSearchHit> hits = FindRanked(query, limit);
        var results = new List<AppItem>(hits.Count);
        foreach (AppSearchHit hit in hits) results.Add(hit.Item);
        return results;
    }

    public IReadOnlyList<AppSearchHit> FindRanked(string query, int limit = 8)
    {
        string normalized = AppItem.Normalize(query.Trim());
        var top = new List<AppSearchHit>(limit);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (AppItem item in _items)
        {
            double match = normalized.Length == 0 ? 1 : Match(item.NormalizedName, normalized);
            if (match == 0) continue;
            _usage.TryGetValue(item.Identity, out UsageRecord usage);
            double score = match + Math.Min(usage.Count, 20) * 0.2;
            if (usage.Count > 0)
                score += 2 / (1 + Math.Max(0, (now - usage.LastLaunch).TotalDays));
            int index = 0;
            while (index < top.Count && top[index].Score >= score) index++;
            if (index >= limit) continue;
            top.Insert(index, new AppSearchHit(item, score));
            if (top.Count > limit) top.RemoveAt(limit);
        }
        return top;
    }

    internal static int Match(string name, string query)
    {
        if (name.StartsWith(query, StringComparison.Ordinal)) return 100;
        int boundary = 0;
        for (int i = 0; i < name.Length && boundary < query.Length; i++)
            if ((i == 0 || !char.IsLetterOrDigit(name[i - 1])) && name[i] == query[boundary]) boundary++;
        if (boundary == query.Length) return 80;
        if (name.Contains(query, StringComparison.Ordinal)) return 60;
        int matched = 0;
        foreach (char c in name)
            if (matched < query.Length && c == query[matched]) matched++;
        return matched == query.Length ? 40 : 0;
    }

}

public readonly record struct UsageRecord(int Count, DateTimeOffset LastLaunch);
public readonly record struct AppSearchHit(AppItem Item, double Score);
