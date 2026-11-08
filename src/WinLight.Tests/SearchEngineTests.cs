using WinLight.Core;

namespace WinLight.Tests;

public sealed class SearchEngineTests
{
    [Fact]
    public async Task MergesWeightedScoresAndReturnsOnlyEight()
    {
        var first = new FakeProvider("first", (_, _) => Completed(
            Enumerable.Range(0, 9).Select(i => Result($"A{i}", "first", 0.9 - i * 0.1)).ToArray()));
        var second = new FakeProvider("second", (_, _) => Completed(Result("B", "second", 0.6)));
        var engine = new SearchEngine([(first, 1d), (second, 2d)], (_, _) => { });

        IReadOnlyList<SearchResult> results = await engine.QueryAsync("a");

        Assert.Equal(8, results.Count);
        Assert.Equal("B", results[0].Title);
        Assert.Equal(["B", "A0", "A1", "A2", "A3", "A4", "A5", "A6"],
            results.Select(result => result.Title));
    }

    [Fact]
    public async Task EqualWeightedScoresKeepProviderAndItemOrder()
    {
        var first = new FakeProvider("first", (_, _) => Completed(
            Result("A", "first", 0.5), Result("B", "first", 0.5)));
        var second = new FakeProvider("second", (_, _) => Completed(Result("C", "second", 1)));
        var engine = new SearchEngine([(first, 2d), (second, 1d)], (_, _) => { });

        IReadOnlyList<SearchResult> results = await engine.QueryAsync("a");

        Assert.Equal(["A", "B", "C"], results.Select(result => result.Title));
    }

    [Fact]
    public async Task SingleProviderResultsAreSortedWhenProviderReturnsUnsortedScores()
    {
        var provider = new FakeProvider("only", (_, _) => Completed(
            Result("Low", "only", 0.2), Result("High", "only", 0.8)));
        var engine = new SearchEngine([(provider, 1d)], (_, _) => { });

        IReadOnlyList<SearchResult> results = await engine.QueryAsync("a");

        Assert.Equal(["High", "Low"], results.Select(result => result.Title));
    }

    [Fact]
    public async Task OneNonemptyProviderAmongTwoUsesItsSortedResultsDirectly()
    {
        SearchResult[] appResults = [Result("App", "apps", 0.9)];
        var apps = new FakeProvider("apps", (_, _) =>
            ValueTask.FromResult<IReadOnlyList<SearchResult>>(appResults));
        var irrelevant = new FakeProvider("irrelevant", (_, _) => Completed());
        var engine = new SearchEngine([(apps, 1d), (irrelevant, 1.1d)], (_, _) => { });

        IReadOnlyList<SearchResult> results = await engine.QueryAsync("app");

        Assert.Same(appResults, results);
    }

    [Fact]
    public async Task ThrowingProviderIsLoggedAndSkipped()
    {
        var errors = new List<string>();
        var broken = new FakeProvider("broken", (_, _) => throw new InvalidOperationException("broken"));
        var good = new FakeProvider("good", (_, _) => Completed(Result("Good", "good", 1)));
        var engine = new SearchEngine([(broken, 1d), (good, 1d)], (message, _) => errors.Add(message));

        IReadOnlyList<SearchResult> results = await engine.QueryAsync("good");

        Assert.Equal("Good", Assert.Single(results).Title);
        Assert.Single(errors);
        Assert.Contains("broken", errors[0]);
    }

    [Fact]
    public async Task NewQueryCancelsPreviousProviderToken()
    {
        var slow = new TaskCompletionSource<IReadOnlyList<SearchResult>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken firstToken = default;
        var provider = new FakeProvider("slow", (query, token) =>
        {
            if (query == "first")
            {
                firstToken = token;
                return new ValueTask<IReadOnlyList<SearchResult>>(slow.Task);
            }
            return Completed(Result("Second", "slow", 1));
        });
        var engine = new SearchEngine([(provider, 1d)], (_, _) => { });

        ValueTask<IReadOnlyList<SearchResult>> oldQuery = engine.QueryAsync("first");
        IReadOnlyList<SearchResult> newResults = await engine.QueryAsync("second");
        slow.SetResult([Result("First", "slow", 1)]);

        Assert.True(firstToken.IsCancellationRequested);
        Assert.Equal("Second", Assert.Single(newResults).Title);
        Assert.Empty(await oldQuery);
    }

    [Fact]
    public async Task CallerCancellationDiscardsSlowResult()
    {
        var slow = new TaskCompletionSource<IReadOnlyList<SearchResult>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeProvider("slow", (_, _) =>
            new ValueTask<IReadOnlyList<SearchResult>>(slow.Task));
        var engine = new SearchEngine([(provider, 1d)], (_, _) => { });
        using var stop = new CancellationTokenSource();

        ValueTask<IReadOnlyList<SearchResult>> query = engine.QueryAsync("first", stop.Token);
        stop.Cancel();
        slow.SetResult([Result("First", "slow", 1)]);

        Assert.Empty(await query);
    }

    [Fact]
    public async Task EmptyQueryShortcutCancelsAndInvalidatesSlowResult()
    {
        var slow = new TaskCompletionSource<IReadOnlyList<SearchResult>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeProvider("slow", (_, _) =>
            new ValueTask<IReadOnlyList<SearchResult>>(slow.Task));
        var engine = new SearchEngine([(provider, 1d)], (_, _) => { });

        ValueTask<IReadOnlyList<SearchResult>> old = engine.QueryAsync("old");
        engine.CancelCurrentQuery();
        slow.SetResult([Result("Stale", "slow", 1)]);

        Assert.Empty(await old);
    }

    private static SearchResult Result(string title, string provider, double score) =>
        new(title, null, null, score, provider,
            new ResultAction.LaunchApp(new AppItem(title, title, title)));

    private static ValueTask<IReadOnlyList<SearchResult>> Completed(params SearchResult[] results) =>
        ValueTask.FromResult<IReadOnlyList<SearchResult>>(results);

    private sealed class FakeProvider(
        string id, Func<string, CancellationToken, ValueTask<IReadOnlyList<SearchResult>>> query) : IResultProvider
    {
        public string Id => id;
        public ValueTask<IReadOnlyList<SearchResult>> QueryAsync(string text, CancellationToken ct) => query(text, ct);
    }
}
