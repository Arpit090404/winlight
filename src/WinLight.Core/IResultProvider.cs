namespace WinLight.Core;

public interface IResultProvider
{
    string Id { get; }
    ValueTask<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct);
}
