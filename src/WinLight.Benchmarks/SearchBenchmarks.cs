using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using WinLight.Core;

namespace WinLight.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class SearchBenchmarks
{
    private AppSearch _search = null!;

    [Params(200, 1000, 5000)]
    public int IndexSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var items = new AppItem[IndexSize];
        for (int index = 0; index < items.Length; index++)
        {
            string category = (index % 8) switch
            {
                0 => "Calculator",
                1 => "Visual Studio Code",
                2 => "Notepad",
                3 => "Calendar",
                4 => "Camera",
                5 => "Video Studio",
                6 => "Notes Manager",
                _ => "Control Center"
            };
            items[index] = new AppItem($"{category} {index}", $"C:\\Apps\\App{index}.exe",
                $"C:\\Apps\\App{index}.exe");
        }
        _search = new AppSearch();
        _search.SetItems(items);
    }

    [Benchmark] public IReadOnlyList<AppItem> PrefixAndRank() => _search.Find("calc");
    [Benchmark] public IReadOnlyList<AppItem> AcronymAndRank() => _search.Find("vsc");
    [Benchmark] public IReadOnlyList<AppItem> NoMatch() => _search.Find("xyz");
}
