namespace WinLight.Core;

public sealed class AppsProvider(AppSearch search) : IResultProvider
{
    public string Id => "apps";

    public ValueTask<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || ct.IsCancellationRequested)
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>(Array.Empty<SearchResult>());

        IReadOnlyList<AppSearchHit> hits = search.FindRanked(query);
        var results = new List<SearchResult>(hits.Count);
        foreach (AppSearchHit hit in hits)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(new SearchResult(hit.Item.Name, null, hit.Item.LaunchPath,
                hit.Score / 106d, Id, new ResultAction.LaunchApp(hit.Item)));
        }
        return ValueTask.FromResult<IReadOnlyList<SearchResult>>(results);
    }
}
