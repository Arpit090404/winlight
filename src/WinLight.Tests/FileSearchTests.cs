using WinLight.Core;
using System.Threading.Channels;

namespace WinLight.Tests;

public sealed class FileSearchTests
{
    [Fact]
    public async Task SearchRanksNamesAndKeepsDuplicateNamesInDifferentFolders()
    {
        var provider = new FileSearchProvider();
        provider.SetEntries([
            new FileEntry(@"C:\Downloads\report.pdf", false),
            new FileEntry(@"C:\Documents\report.docx", false),
            new FileEntry(@"C:\Documents\old-report.txt", false),
            new FileEntry(@"C:\Documents\Reports", true),
            new FileEntry(@"C:\Downloads\receipt.pdf", false),
        ]);
        IReadOnlyList<SearchResult> prefix = await provider.QueryAsync("report", default);
        Assert.Equal(4, prefix.Count);
        Assert.All(prefix.Take(3), hit => Assert.Equal(1, hit.Score));
        Assert.IsType<ResultAction.OpenFile>(prefix[2].Action);
        Assert.Equal(0.6, prefix[3].Score);
        Assert.Equal(3, (await provider.QueryAsync("report.", default)).Count);
        Assert.NotEmpty(await provider.QueryAsync("rpt", default));
        Assert.Empty(await provider.QueryAsync("nomatch", default));
        Assert.Empty(await provider.QueryAsync(" ", default));
    }

    [Fact]
    public async Task SearchIsCancellationAwareAndDeduplicatesPaths()
    {
        var provider = new FileSearchProvider();
        FileEntry entry = new(@"C:\Downloads\one.txt", false);
        provider.SetEntries([entry, entry]);
        Assert.Single(await provider.QueryAsync("one", default));
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Assert.Empty(await provider.QueryAsync("one", stop.Token));
    }

    [Fact]
    public async Task CharacterPrefilterPreservesDiacriticAndUnicodeMatches()
    {
        var provider = new FileSearchProvider();
        provider.SetEntries([new FileEntry(@"C:\Documents\Café.txt", false),
            new FileEntry(@"C:\Documents\東京.txt", false)]);
        Assert.Single(await provider.QueryAsync("cafe", default));
        Assert.Single(await provider.QueryAsync("東京", default));
    }

    [Fact]
    public async Task EightPrefixMatchesBoundResultsAheadOfWeakerMatches()
    {
        var provider = new FileSearchProvider();
        provider.SetEntries(Enumerable.Range(0, 12)
            .Select(i => new FileEntry($@"C:\Documents\note-{i}.txt", false))
            .Append(new FileEntry(@"C:\Documents\old-note.txt", false)));
        IReadOnlyList<SearchResult> hits = await provider.QueryAsync("note", default);
        Assert.Equal(8, hits.Count);
        Assert.All(hits, hit => Assert.Equal(1, hit.Score));
    }

    [Fact]
    public async Task FileProviderPreservesAppAndCalculatorPriority()
    {
        var apps = new AppSearch();
        apps.SetItems([new AppItem("Notepad", "notepad.exe", "notepad")]);
        var files = new FileSearchProvider();
        files.SetEntries([
            new FileEntry(@"C:\Downloads\Notepad.txt", false),
            new FileEntry(@"C:\Downloads\12*8.txt", false)
        ]);
        var engine = new SearchEngine([(new AppsProvider(apps), 1d),
            (new CalculatorProvider(), 1.1d), (files, 0.7d)], (_, _) => { });
        Assert.Equal("apps", (await engine.QueryAsync("notepad"))[0].ProviderId);
        Assert.Equal("calculator", (await engine.QueryAsync("12*8"))[0].ProviderId);
        Assert.Equal("files", (await engine.QueryAsync("notepad.txt"))[0].ProviderId);
    }

    [Fact]
    public async Task IndexesOnlyTwoRootsAndSkipsDirectoryLinks()
    {
        using var area = new TempArea();
        string downloads = area.CreateDirectory("Downloads");
        string documents = area.CreateDirectory("Documents");
        string outside = area.CreateDirectory("Elsewhere");
        Directory.CreateDirectory(Path.Combine(downloads, "Nested"));
        File.WriteAllText(Path.Combine(downloads, "Nested", "alpha.pdf"), "a");
        File.WriteAllText(Path.Combine(documents, "beta.txt"), "b");
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "c");
        try { Directory.CreateSymbolicLink(Path.Combine(downloads, "Link"), outside); }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException) { }
        var provider = new FileSearchProvider();
        using var index = new FileIndexService([downloads, documents], provider, (_, _) => { });
        await index.InitialScan.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(3, provider.Count);
        Assert.Single(await provider.QueryAsync("alpha", default));
        Assert.Single(await provider.QueryAsync("nested", default));
        Assert.Single(await provider.QueryAsync("beta", default));
        Assert.Empty(await provider.QueryAsync("secret", default));
    }

    [Fact]
    public async Task IndexesDocumentFilesAndAllFoldersButSkipsOtherFileTypes()
    {
        using var area = new TempArea();
        string downloads = area.CreateDirectory("Downloads");
        string folder = Directory.CreateDirectory(Path.Combine(downloads, "Scripts")).FullName;
        string[] allowed = [".docx", ".pdf", ".xls", ".xlsx", ".xlsm", ".xlsb",
            ".md", ".txt", ".ppt", ".pptx"];
        foreach (string extension in allowed)
            File.WriteAllText(Path.Combine(downloads, "findme" + extension), "x");
        File.WriteAllText(Path.Combine(downloads, "findme.exe"), "x");
        File.WriteAllText(Path.Combine(folder, "findme.png"), "x");

        var provider = new FileSearchProvider();
        using var index = new FileIndexService([downloads], provider, (_, _) => { });
        await index.InitialScan.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(allowed.Length + 1, provider.Count);
        Assert.Single(await provider.QueryAsync("scripts", default));
        Assert.Empty(await provider.QueryAsync("findme.exe", default));
        Assert.Empty(await provider.QueryAsync("findme.png", default));
        foreach (string extension in allowed)
            Assert.Single(await provider.QueryAsync("findme" + extension, default));
    }

    [Fact]
    public async Task MissingRootDoesNotBreakAvailableRoot()
    {
        using var area = new TempArea();
        string downloads = area.CreateDirectory("Downloads");
        File.WriteAllText(Path.Combine(downloads, "found.txt"), "x");
        var provider = new FileSearchProvider();
        using var index = new FileIndexService([downloads, Path.Combine(area.Path, "Missing")],
            provider, (_, _) => { });
        await index.InitialScan.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(await provider.QueryAsync("found", default));
    }

    [Fact]
    public async Task RootCreatedAfterStartupIsIndexed()
    {
        using var area = new TempArea();
        string missing = Path.Combine(area.Path, "Downloads");
        var provider = new FileSearchProvider();
        var updates = Channel.CreateUnbounded<bool>();
        using var index = new FileIndexService([missing], provider, (_, _) => { },
            () => updates.Writer.TryWrite(true));
        await index.InitialScan.WaitAsync(TimeSpan.FromSeconds(10));
        Directory.CreateDirectory(missing);
        File.WriteAllText(Path.Combine(missing, "arrived.txt"), "x");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while ((await provider.QueryAsync("arrived", default)).Count == 0)
            await updates.Reader.ReadAsync(timeout.Token);
    }

    [Fact]
    public async Task WatcherAppliesCreateRenameMoveAndDelete()
    {
        using var area = new TempArea();
        string downloads = area.CreateDirectory("Downloads");
        string documents = area.CreateDirectory("Documents");
        var provider = new FileSearchProvider();
        var updates = Channel.CreateUnbounded<bool>();
        using var index = new FileIndexService([downloads, documents], provider,
            (_, _) => { }, () => updates.Writer.TryWrite(true));
        await index.InitialScan.WaitAsync(TimeSpan.FromSeconds(10));

        string ignored = Path.Combine(downloads, "ignored.exe");
        File.WriteAllText(ignored, "x");
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            await updates.Reader.ReadAsync(timeout.Token);
        Assert.Empty(await provider.QueryAsync("ignored", default));

        string accepted = Path.ChangeExtension(ignored, ".PDF");
        File.Move(ignored, accepted);
        await UntilAsync(() => provider.QueryAsync("ignored", default).Result.Count == 1);
        File.Move(accepted, ignored);
        await UntilAsync(() => provider.QueryAsync("ignored", default).Result.Count == 0);

        string first = Path.Combine(downloads, "new.pdf");
        File.WriteAllText(first, "one");
        await UntilAsync(() => provider.QueryAsync("new.pdf", default).Result.Count == 1);

        string renamed = Path.Combine(downloads, "renamed.pdf");
        File.Move(first, renamed);
        await UntilAsync(() => provider.QueryAsync("renamed.pdf", default).Result.Count == 1 &&
            provider.QueryAsync("new.pdf", default).Result.Count == 0);

        string moved = Path.Combine(documents, "renamed.pdf");
        File.Move(renamed, moved);
        await UntilAsync(() =>
        {
            IReadOnlyList<SearchResult> hits = provider.QueryAsync("renamed.pdf", default).Result;
            return hits.Count == 1 && ((ResultAction.OpenFile)hits[0].Action).Path == moved;
        });

        File.Delete(moved);
        await UntilAsync(() => provider.QueryAsync("renamed.pdf", default).Result.Count == 0);

        string folder = Path.Combine(documents, "Project");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "nested.txt"), "nested");
        await UntilAsync(() => provider.QueryAsync("project", default).Result.Count == 1 &&
            provider.QueryAsync("nested.txt", default).Result.Count == 1);
        Directory.Delete(folder, true);
        await UntilAsync(() => provider.QueryAsync("project", default).Result.Count == 0 &&
            provider.QueryAsync("nested.txt", default).Result.Count == 0);

        async Task UntilAsync(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!condition()) await updates.Reader.ReadAsync(timeout.Token);
        }
    }

    private sealed class TempArea : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "WinLightTests", Guid.NewGuid().ToString("N"));
        public TempArea() => Directory.CreateDirectory(Path);
        public string CreateDirectory(string name) => Directory.CreateDirectory(
            System.IO.Path.Combine(Path, name)).FullName;
        public void Dispose() => Directory.Delete(Path, true);
    }
}

