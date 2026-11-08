namespace WinLight.Core;

public sealed class SearchEngine
{
    private readonly (IResultProvider Provider, double Weight)[] _providers;
    private readonly Action<string, Exception> _logError;
    private readonly object _gate = new();
    private CancellationTokenSource? _active;
    private long _queryId;

    public SearchEngine(IEnumerable<(IResultProvider Provider, double Weight)> providers,
        Action<string, Exception> logError)
    {
        _providers = providers.ToArray();
        _logError = logError;
        foreach (var entry in _providers)
            if (!double.IsFinite(entry.Weight) || entry.Weight < 0)
                throw new ArgumentOutOfRangeException(nameof(providers), "Weights must be finite and nonnegative.");
    }

    public ValueTask<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct = default)
    {
        CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(ct);
        CancellationTokenSource? previous;
        long id;
        lock (_gate)
        {
            previous = _active;
            _active = source;
            id = ++_queryId;
        }
        try { previous?.Cancel(); }
        catch (ObjectDisposedException) { }
        if (source.IsCancellationRequested) return Finish(source, id, []);

        List<(SearchResult Result, double WeightedScore, int Order)>? merged = null;
        IReadOnlyList<SearchResult>? sole = null;
        double soleWeight = 0;
        for (int i = 0; i < _providers.Length; i++)
        {
            var entry = _providers[i];
            try
            {
                ValueTask<IReadOnlyList<SearchResult>> pending = entry.Provider.QueryAsync(query, source.Token);
                if (!pending.IsCompletedSuccessfully)
                {
                    merged ??= new List<(SearchResult Result, double WeightedScore, int Order)>(8);
                    if (sole is not null) Add(merged, sole, soleWeight);
                    return AwaitRemaining(query, pending, i, source, id, merged);
                }
                IReadOnlyList<SearchResult> results = pending.Result;
                if (results.Count == 0) continue;
                if (merged is null && sole is null)
                {
                    sole = results;
                    soleWeight = entry.Weight;
                    continue;
                }
                merged ??= new List<(SearchResult Result, double WeightedScore, int Order)>(8);
                if (sole is not null)
                {
                    Add(merged, sole, soleWeight);
                    sole = null;
                }
                Add(merged, results, entry.Weight);
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested)
            {
                return Finish(source, id, []);
            }
            catch (Exception error) { _logError($"Query provider {entry.Provider.Id}", error); }
        }
        if (merged is not null) return Finish(source, id, Merge(merged));
        if (sole is null) return Finish(source, id, []);
        if (soleWeight == 1 && IsDirectResult(sole)) return Finish(source, id, sole);
        merged = new List<(SearchResult Result, double WeightedScore, int Order)>(sole.Count);
        Add(merged, sole, soleWeight);
        return Finish(source, id, Merge(merged));
    }

    public void CancelCurrentQuery()
    {
        CancellationTokenSource? active;
        lock (_gate)
        {
            active = _active;
            _active = null;
            _queryId++;
        }
        try { active?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private async ValueTask<IReadOnlyList<SearchResult>> AwaitRemaining(string query,
        ValueTask<IReadOnlyList<SearchResult>> pending, int index, CancellationTokenSource source,
        long id, List<(SearchResult Result, double WeightedScore, int Order)> merged)
    {
        for (int i = index; i < _providers.Length; i++)
        {
            var entry = _providers[i];
            try
            {
                IReadOnlyList<SearchResult> results = await pending.ConfigureAwait(false);
                Add(merged, results, entry.Weight);
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested)
            {
                return Complete(source, id, []);
            }
            catch (Exception error) { _logError($"Query provider {entry.Provider.Id}", error); }
            if (source.IsCancellationRequested) return Complete(source, id, []);
            if (i + 1 < _providers.Length)
            {
                try { pending = _providers[i + 1].Provider.QueryAsync(query, source.Token); }
                catch (Exception error)
                {
                    _logError($"Query provider {_providers[i + 1].Provider.Id}", error);
                    pending = ValueTask.FromResult<IReadOnlyList<SearchResult>>([]);
                }
            }
        }
        return Complete(source, id, Merge(merged));
    }

    private ValueTask<IReadOnlyList<SearchResult>> Finish(CancellationTokenSource source, long id,
        IReadOnlyList<SearchResult> results)
        => ValueTask.FromResult(Complete(source, id, results));

    private IReadOnlyList<SearchResult> Complete(CancellationTokenSource source, long id,
        IReadOnlyList<SearchResult> results)
    {
        bool current;
        lock (_gate)
        {
            current = id == _queryId && !source.IsCancellationRequested;
            if (ReferenceEquals(_active, source)) _active = null;
        }
        source.Dispose();
        return current ? results : Array.Empty<SearchResult>();
    }

    private static void Add(List<(SearchResult Result, double WeightedScore, int Order)> merged,
        IReadOnlyList<SearchResult> results, double weight)
    {
        foreach (SearchResult result in results)
        {
            if (!double.IsFinite(result.Score) || result.Score < 0 || result.Score > 1)
                continue;
            merged.Add((result, result.Score * weight, merged.Count));
        }
    }

    private static bool IsDirectResult(IReadOnlyList<SearchResult> results)
    {
        if (results.Count > 8) return false;
        double previousScore = double.PositiveInfinity;
        foreach (SearchResult result in results)
        {
            if (!double.IsFinite(result.Score) || result.Score < 0 || result.Score > 1)
                return false;
            if (result.Score > previousScore) return false;
            previousScore = result.Score;
        }
        return true;
    }

    private static IReadOnlyList<SearchResult> Merge(
        List<(SearchResult Result, double WeightedScore, int Order)> merged)
    {
        merged.Sort(static (a, b) =>
        {
            int score = b.WeightedScore.CompareTo(a.WeightedScore);
            return score != 0 ? score : a.Order.CompareTo(b.Order);
        });
        int count = Math.Min(8, merged.Count);
        var results = new List<SearchResult>(count);
        for (int i = 0; i < count; i++) results.Add(merged[i].Result);
        return results;
    }
}
