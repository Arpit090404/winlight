namespace WinLight.Core;

public sealed class FileEntry
{
    public FileEntry(string path, bool isDirectory)
    {
        Name = System.IO.Path.GetFileName(path);
        _anchorPath = System.IO.Path.GetDirectoryName(path);
        IsDirectory = isDirectory;
        NormalizedName = AppItem.Normalize(Name);
        SearchMask = Mask(NormalizedName);
        if (isDirectory) Children = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
    }

    private FileEntry(string name, FileEntry parent, bool isDirectory)
    {
        Name = name;
        ParentNode = parent;
        IsDirectory = isDirectory;
        NormalizedName = AppItem.Normalize(name);
        SearchMask = Mask(NormalizedName);
        if (isDirectory) Children = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
    }

    private FileEntry(string rootPath)
    {
        _anchorPath = rootPath;
        Name = string.Empty;
        NormalizedName = string.Empty;
        IsDirectory = true;
        Children = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
    }

    private readonly string? _anchorPath;
    internal FileEntry? ParentNode { get; }
    internal Dictionary<string, FileEntry>? Children { get; }
    internal static FileEntry Root(string path) => new(path);
    internal static FileEntry Child(string name, FileEntry parent, bool isDirectory) =>
        new(name, parent, isDirectory);

    public string Path => Name.Length == 0 ? _anchorPath! :
        System.IO.Path.Combine(ParentNode?.Path ?? _anchorPath!, Name);
    public bool IsDirectory { get; }
    public string Name { get; }
    public string Parent => ParentNode?.Path ?? _anchorPath ?? string.Empty;
    public string NormalizedName { get; }
    public ulong SearchMask { get; }

    internal static ulong Mask(ReadOnlySpan<char> value)
    {
        ulong mask = 0;
        foreach (char letter in value)
        {
            if (letter is >= 'A' and <= 'Z') mask |= 1UL << (letter - 'A');
            else if (letter is >= '0' and <= '9') mask |= 1UL << (26 + letter - '0');
        }
        return mask;
    }
}

public sealed class FileSearchProvider : IResultProvider
{
    private FileEntry[] _entries = [];
    public string Id => "files";
    public int Count => Volatile.Read(ref _entries).Length;

    public void SetEntries(IEnumerable<FileEntry> entries)
    {
        var unique = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (FileEntry entry in entries) unique[entry.Path] = entry;
        PublishUnique(unique.Values);
    }

    internal void PublishUnique(ICollection<FileEntry> entries) =>
        Volatile.Write(ref _entries, [.. entries]);

    public ValueTask<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || ct.IsCancellationRequested || query.Length > 256 ||
            query.AsSpan().IndexOfAny("<>:\"/\\|?*".AsSpan()) >= 0 || query[0] == '=')
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>([]);
        string normalized = AppItem.Normalize(query.Trim());
        if (normalized.Length == 0) return ValueTask.FromResult<IReadOnlyList<SearchResult>>([]);
        ulong queryMask = FileEntry.Mask(normalized);
        var top = new List<(FileEntry Entry, int Match)>(8);
        foreach (FileEntry entry in Volatile.Read(ref _entries))
        {
            ct.ThrowIfCancellationRequested();
            if ((entry.SearchMask & queryMask) != queryMask) continue;
            int match = AppSearch.Match(entry.NormalizedName, normalized);
            if (match == 0) continue;
            int position = 0;
            while (position < top.Count && top[position].Match >= match) position++;
            if (position >= 8) continue;
            top.Insert(position, (entry, match));
            if (top.Count > 8) top.RemoveAt(8);
            if (top.Count == 8 && top[7].Match == 100) break;
        }
        var results = new List<SearchResult>(top.Count);
        foreach (var hit in top)
            results.Add(new SearchResult(hit.Entry.Name, hit.Entry.Parent, hit.Entry.Path,
                hit.Match / 100d, Id, new ResultAction.OpenFile(hit.Entry.Path, hit.Entry.IsDirectory)));
        return ValueTask.FromResult<IReadOnlyList<SearchResult>>(results);
    }
}
