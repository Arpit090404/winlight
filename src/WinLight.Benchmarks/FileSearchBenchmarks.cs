using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using WinLight.Core;

namespace WinLight.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class FileSearchBenchmarks
{
    private FileSearchProvider _provider = null!;

    [Params(5000, 20000, 250000)]
    public int IndexSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var files = new FileEntry[IndexSize];
        for (int i = 0; i < files.Length; i++)
        {
            string name = $"Document-{i:D5}.pdf";
            string parent = $@"C:\Users\Sample\Documents\Folder{i % 100}";
            files[i] = new FileEntry(Path.Combine(parent, name), false);
        }
        _provider = new FileSearchProvider();
        _provider.SetEntries(files);
    }

    [Benchmark] public ValueTask<IReadOnlyList<SearchResult>> Prefix() =>
        _provider.QueryAsync("document-4", default);

    [Benchmark] public ValueTask<IReadOnlyList<SearchResult>> NoMatch() =>
        _provider.QueryAsync("not-present", default);
}
